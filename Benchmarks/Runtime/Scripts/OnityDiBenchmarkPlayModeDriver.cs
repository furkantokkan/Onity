using System;
using System.Collections;
using UnityEngine;

namespace Onity.Benchmarks
{
    public sealed class OnityDiBenchmarkPlayModeDriver : MonoBehaviour
    {
        private string m_latestJson;
        private string m_allocationRawPath;

        public static void Run(string latestJson)
        {
            if (string.IsNullOrEmpty(latestJson))
            {
                throw new ArgumentException("Benchmark output path cannot be null or empty.", nameof(latestJson));
            }

            GameObject runner = new GameObject("Onity DI Benchmark Play Mode Driver");
            DontDestroyOnLoad(runner);

            OnityDiBenchmarkPlayModeDriver driver = runner.AddComponent<OnityDiBenchmarkPlayModeDriver>();
            driver.m_latestJson = latestJson;
        }

        internal static void RunAllocationCapture(string rawProfilePath)
        {
            if (string.IsNullOrEmpty(rawProfilePath))
            {
                throw new ArgumentException("Profiler output path cannot be null or empty.", nameof(rawProfilePath));
            }

            GameObject runner = new GameObject("Onity DI Allocation Capture Driver");
            DontDestroyOnLoad(runner);

            OnityDiBenchmarkPlayModeDriver driver = runner.AddComponent<OnityDiBenchmarkPlayModeDriver>();
            driver.m_allocationRawPath = rawProfilePath;
        }

        private IEnumerator Start()
        {
            yield return null;

            if (m_allocationRawPath != null)
            {
                int exitCode = 0;
                bool started = false;

                try
                {
                    OnityDiBenchmarkPlayerRunner.StartAllocationCapture(m_allocationRawPath);
                    started = true;
                }
                catch (Exception exception)
                {
                    exitCode = 1;
                    Debug.LogException(exception, this);
                }

                if (started)
                {
                    yield return null;

                    for (int step = 0; step < OnityDiBenchmarkPlayerRunner.AllocationCaptureStepCount; step++)
                    {
                        try
                        {
                            OnityDiBenchmarkPlayerRunner.RunAllocationCaptureStep(step);
                        }
                        catch (Exception exception)
                        {
                            exitCode = 1;
                            Debug.LogException(exception, this);
                            break;
                        }

                        // Let the Profiler finish the frame before writing the next marker.
                        yield return null;
                    }

                    try
                    {
                        OnityDiBenchmarkPlayerRunner.StopAllocationCapture();
                    }
                    catch (Exception exception)
                    {
                        exitCode = 1;
                        Debug.LogException(exception, this);
                    }
                }

                Application.Quit(exitCode);
                yield break;
            }

            try
            {
                OnityDiBenchmarkPlayerRunner.RunProfilerCaptureAndSave(m_latestJson);
                Debug.Log($"Onity DI Play Mode profiler capture completed. Latest report: {m_latestJson}", this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                Destroy(gameObject);
            }
        }
    }
}
