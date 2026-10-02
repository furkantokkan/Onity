using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    internal sealed class OnityEndOfFrameRunner : MonoBehaviour
    {
        /// <summary>
        /// A queued end-of-frame wait with the version of the cycle that queued it, so an entry whose cycle
        /// published elsewhere (an immediate cancellation) never touches a later rental of the source.
        /// </summary>
        internal struct PendingWait
        {
            internal OnityEndOfFrameTaskSource Source;
            internal int Version;
        }

        private static readonly WaitForEndOfFrame s_wait = new WaitForEndOfFrame();
        private List<PendingWait> m_pending = new List<PendingWait>(32);
        private Coroutine m_coroutine;
        private ulong m_pass;
        private int m_pumpGeneration;
        private bool m_retired;

        internal void StartPump()
        {
            int generation = unchecked(++m_pumpGeneration);
            m_coroutine = StartCoroutine(Pump(generation));
            if (m_coroutine == null)
            {
                throw new InvalidOperationException("The Onity end-of-frame coroutine could not start.");
            }
        }

        internal OnityTask Schedule(CancellationToken token, bool cancelImmediately)
        {
            OnityEndOfFrameTaskSource source = OnityEndOfFrameTaskSource.Rent(m_pass, token, cancelImmediately);
            int version = source.Version;
            m_pending.Add(new PendingWait { Source = source, Version = version });
            return new OnityTask(source, version);
        }

        private IEnumerator Pump(int generation)
        {
            while (!m_retired && generation == m_pumpGeneration)
            {
                // The coroutine owns no individual request across this rendering yield.
                yield return s_wait;
                if (m_retired || generation != m_pumpGeneration)
                {
                    yield break;
                }

                m_pass++;
                int count = m_pending.Count;
                for (int i = count - 1; i >= 0; i--)
                {
                    PendingWait wait = m_pending[i];
                    if (!wait.Source.IsAwaitingPublication(wait.Version))
                    {
                        // Published by an immediate cancellation.
                        RemoveAt(i);
                        continue;
                    }

                    if (wait.Source.RegisteredPass != m_pass)
                    {
                        bool canceled = wait.Source.IsCancellationFlagged;
                        RemoveAt(i);
                        Publish(wait, canceled, null);
                        if (m_retired || generation != m_pumpGeneration)
                        {
                            yield break;
                        }
                    }
                }
            }
        }

        internal void CancelPending()
        {
            if (m_retired)
            {
                return;
            }

            int generation = m_pumpGeneration;
            int count = m_pending.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                PendingWait wait = m_pending[i];
                if (!wait.Source.IsAwaitingPublication(wait.Version))
                {
                    RemoveAt(i);
                    continue;
                }

                if (wait.Source.IsCancellationFlagged)
                {
                    RemoveAt(i);
                    Publish(wait, true, null);
                    if (m_retired || generation != m_pumpGeneration)
                    {
                        return;
                    }
                }
            }
        }

        private void RemoveAt(int index)
        {
            int last = m_pending.Count - 1;
            m_pending[index] = m_pending[last];
            m_pending.RemoveAt(last);
        }

        internal List<PendingWait> Detach()
        {
            if (m_retired)
            {
                return null;
            }

            m_retired = true;
            // Invalidate before stopping: an executing publication may return into
            // the old iterator after its callback has created a replacement host.
            m_pumpGeneration++;
            List<PendingWait> pending = m_pending;
            m_pending = null;
            Coroutine coroutine = m_coroutine;
            m_coroutine = null;
            if (coroutine != null)
            {
                try
                {
                    StopCoroutine(coroutine);
                }
                catch (Exception exception)
                {
                    Log(exception);
                }
            }
            return pending;
        }

        internal static void RetirePending(List<PendingWait> pending, Exception failure)
        {
            if (pending == null)
            {
                return;
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingWait wait = pending[i];
                pending.RemoveAt(i);
                if (wait.Source.IsAwaitingPublication(wait.Version))
                {
                    Publish(wait, failure == null, failure);
                }
            }
        }

        private static void Publish(PendingWait wait, bool canceled, Exception failure)
        {
            try
            {
                wait.Source.Publish(wait.Version, canceled, failure);
            }
            catch (Exception exception)
            {
                Log(exception);
            }
        }

        private static void Log(Exception exception)
        {
            try
            {
                Debug.LogException(exception);
            }
            catch (Exception)
            {
            }
        }

        private void OnDisable()
        {
            OnityTaskPlayerLoop.RetireEndOfFrame(this);
        }

        private void OnDestroy()
        {
            OnityTaskPlayerLoop.RetireEndOfFrame(this);
        }
    }
}
