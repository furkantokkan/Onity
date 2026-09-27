using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Onity.Unity.Async
{
    internal sealed class OnityEndOfFrameRunner : MonoBehaviour
    {
        private static readonly WaitForEndOfFrame s_wait = new WaitForEndOfFrame();
        private List<OnityEndOfFrameTaskSource> m_pending =
            new List<OnityEndOfFrameTaskSource>(32);
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

        internal OnityTask Schedule(CancellationToken token)
        {
            OnityEndOfFrameTaskSource source = OnityEndOfFrameTaskSource.Rent(m_pass, token);
            OnityTask task = new OnityTask(source);
            m_pending.Add(source);
            return task;
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
                    OnityEndOfFrameTaskSource source = m_pending[i];
                    if (source.RegisteredPass != m_pass)
                    {
                        bool canceled = source.IsCancellationFlagged;
                        RemoveAt(i);
                        Publish(source, canceled, null);
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
                OnityEndOfFrameTaskSource source = m_pending[i];
                if (source.IsCancellationFlagged)
                {
                    RemoveAt(i);
                    Publish(source, true, null);
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

        internal List<OnityEndOfFrameTaskSource> Detach()
        {
            if (m_retired)
            {
                return null;
            }

            m_retired = true;
            // Invalidate before stopping: an executing publication may return into
            // the old iterator after its callback has created a replacement host.
            m_pumpGeneration++;
            List<OnityEndOfFrameTaskSource> pending = m_pending;
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

        internal static void RetirePending(List<OnityEndOfFrameTaskSource> pending, Exception failure)
        {
            if (pending == null)
            {
                return;
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                OnityEndOfFrameTaskSource source = pending[i];
                pending.RemoveAt(i);
                Publish(source, failure == null, failure);
            }
        }

        private static void Publish(OnityEndOfFrameTaskSource source, bool canceled, Exception failure)
        {
            try
            {
                source.Publish(canceled, failure);
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
