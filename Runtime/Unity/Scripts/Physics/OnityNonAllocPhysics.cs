using System;
using UnityEngine;

namespace Onity.Unity.Physics
{
    /// <summary>
    /// Allocation-free wrappers over Unity Physics non-alloc query APIs.
    /// </summary>
    public static class OnityNonAllocPhysics
    {
        /// <summary>
        /// Performs non-alloc raycast with caller-owned result buffer.
        /// </summary>
        /// <param name="ray">Ray to cast.</param>
        /// <param name="results">Caller-owned hit buffer.</param>
        /// <param name="maxDistance">Max cast distance.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="queryTriggerInteraction">Trigger query behavior.</param>
        /// <returns>Number of hits written to <paramref name="results" />.</returns>
        public static int Raycast(
            in Ray ray,
            RaycastHit[] results,
            float maxDistance = Mathf.Infinity,
            int layerMask = UnityEngine.Physics.DefaultRaycastLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            ValidateResults(results);
            return UnityEngine.Physics.RaycastNonAlloc(
                ray,
                results,
                maxDistance,
                layerMask,
                queryTriggerInteraction);
        }

        /// <summary>
        /// Performs non-alloc raycast with caller-owned result buffer.
        /// </summary>
        /// <param name="origin">Ray origin.</param>
        /// <param name="direction">Ray direction.</param>
        /// <param name="results">Caller-owned hit buffer.</param>
        /// <param name="maxDistance">Max cast distance.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="queryTriggerInteraction">Trigger query behavior.</param>
        /// <returns>Number of hits written to <paramref name="results" />.</returns>
        public static int Raycast(
            Vector3 origin,
            Vector3 direction,
            RaycastHit[] results,
            float maxDistance = Mathf.Infinity,
            int layerMask = UnityEngine.Physics.DefaultRaycastLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            ValidateResults(results);
            return UnityEngine.Physics.RaycastNonAlloc(
                origin,
                direction,
                results,
                maxDistance,
                layerMask,
                queryTriggerInteraction);
        }

        /// <summary>
        /// Performs non-alloc sphere cast with caller-owned result buffer.
        /// </summary>
        /// <param name="origin">Sphere cast origin.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <param name="direction">Cast direction.</param>
        /// <param name="results">Caller-owned hit buffer.</param>
        /// <param name="maxDistance">Max cast distance.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="queryTriggerInteraction">Trigger query behavior.</param>
        /// <returns>Number of hits written to <paramref name="results" />.</returns>
        public static int SphereCast(
            Vector3 origin,
            float radius,
            Vector3 direction,
            RaycastHit[] results,
            float maxDistance = Mathf.Infinity,
            int layerMask = UnityEngine.Physics.DefaultRaycastLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            ValidateResults(results);
            return UnityEngine.Physics.SphereCastNonAlloc(
                origin,
                radius,
                direction,
                results,
                maxDistance,
                layerMask,
                queryTriggerInteraction);
        }

        /// <summary>
        /// Performs non-alloc overlap sphere with caller-owned collider buffer.
        /// </summary>
        /// <param name="position">Overlap center.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <param name="results">Caller-owned collider buffer.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="queryTriggerInteraction">Trigger query behavior.</param>
        /// <returns>Number of colliders written to <paramref name="results" />.</returns>
        public static int OverlapSphere(
            Vector3 position,
            float radius,
            Collider[] results,
            int layerMask = UnityEngine.Physics.AllLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            return UnityEngine.Physics.OverlapSphereNonAlloc(
                position,
                radius,
                results,
                layerMask,
                queryTriggerInteraction);
        }

        /// <summary>
        /// Performs non-alloc overlap box with caller-owned collider buffer.
        /// </summary>
        /// <param name="center">Overlap center.</param>
        /// <param name="halfExtents">Box half extents.</param>
        /// <param name="results">Caller-owned collider buffer.</param>
        /// <param name="orientation">Box rotation.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="queryTriggerInteraction">Trigger query behavior.</param>
        /// <returns>Number of colliders written to <paramref name="results" />.</returns>
        public static int OverlapBox(
            Vector3 center,
            Vector3 halfExtents,
            Collider[] results,
            Quaternion orientation = default,
            int layerMask = UnityEngine.Physics.AllLayers,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            return UnityEngine.Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                results,
                orientation,
                layerMask,
                queryTriggerInteraction);
        }

        private static void ValidateResults(RaycastHit[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }
        }
    }
}
