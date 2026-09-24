#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using Onity.Core;
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Onity.Unity.Input
{
    /// <summary>
    /// InputSystem-driven reactive input router with context stack support.
    /// </summary>
    public sealed class OnityReactiveInputPlayer : IDisposable
    {
        private readonly InputActionAsset m_actionAsset;
        private readonly Stack<InputActionMap> m_contextStack;
        private readonly List<InputActionMap> m_tempMapBuffer;
        private readonly Dictionary<OnityInputActionKey, Subject<Unit>> m_buttonSubjects;
        private readonly Dictionary<OnityInputActionKey, Subject<Vector2>> m_vector2Subjects;
        private readonly Dictionary<OnityInputActionKey, Subject<float>> m_floatSubjects;
        private readonly Dictionary<OnityInputActionKey, Subject<Unit>> m_longPressSubjects;
        private readonly Dictionary<OnityInputActionKey, Subject<float>> m_longPressProgressSubjects;
        private readonly Dictionary<Guid, ActionBindingState> m_bindingByActionId;
        private readonly List<InputAction> m_boundActions;
        private readonly List<ActionBindingState> m_longPressTrackingStates;
        private readonly float m_longPressDurationSeconds;
        private readonly IDisposable m_updateSubscription;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new reactive input player.
        /// </summary>
        /// <param name="actionAsset">Input action asset.</param>
        /// <param name="longPressDurationSeconds">Default long-press duration.</param>
        public OnityReactiveInputPlayer(InputActionAsset actionAsset, float longPressDurationSeconds = 0.35f)
        {
            if (actionAsset == null)
            {
                throw new ArgumentNullException(nameof(actionAsset));
            }

            if (longPressDurationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(longPressDurationSeconds));
            }

            m_actionAsset = actionAsset;
            m_contextStack = new Stack<InputActionMap>(8);
            m_tempMapBuffer = new List<InputActionMap>(8);
            m_buttonSubjects = new Dictionary<OnityInputActionKey, Subject<Unit>>(64);
            m_vector2Subjects = new Dictionary<OnityInputActionKey, Subject<Vector2>>(64);
            m_floatSubjects = new Dictionary<OnityInputActionKey, Subject<float>>(64);
            m_longPressSubjects = new Dictionary<OnityInputActionKey, Subject<Unit>>(64);
            m_longPressProgressSubjects = new Dictionary<OnityInputActionKey, Subject<float>>(64);
            m_bindingByActionId = new Dictionary<Guid, ActionBindingState>(128);
            m_boundActions = new List<InputAction>(128);
            m_longPressTrackingStates = new List<ActionBindingState>(64);
            m_longPressDurationSeconds = longPressDurationSeconds;
            m_updateSubscription = OnityUnityObservable.EveryUpdate().Subscribe(_ => TickLongPressTracking());
            m_isDisposed = false;

            BuildActionBindings();
        }

        /// <summary>
        /// Current active context name.
        /// </summary>
        public string ActiveContextName => m_contextStack.Count > 0 ? m_contextStack.Peek().name : string.Empty;

        /// <summary>
        /// Pushes one action map context to top of stack.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        public void PushContext(string actionMapName)
        {
            EnsureNotDisposed();
            InputActionMap actionMap = FindActionMap(actionMapName);

            if (m_contextStack.Count > 0 && ReferenceEquals(m_contextStack.Peek(), actionMap))
            {
                return;
            }

            RemoveContextIfPresent(actionMap);
            m_contextStack.Push(actionMap);
            RefreshEnabledMaps();
        }

        /// <summary>
        /// Pops the active action map context.
        /// </summary>
        /// <returns>True when one context is popped; otherwise false.</returns>
        public bool PopContext()
        {
            EnsureNotDisposed();

            if (m_contextStack.Count == 0)
            {
                return false;
            }

            m_contextStack.Pop();
            RefreshEnabledMaps();
            return true;
        }

        /// <summary>
        /// Clears current stack and sets one action map context as active.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        public void SetContext(string actionMapName)
        {
            EnsureNotDisposed();
            InputActionMap actionMap = FindActionMap(actionMapName);
            m_contextStack.Clear();
            m_contextStack.Push(actionMap);
            RefreshEnabledMaps();
        }

        /// <summary>
        /// Clears all contexts and disables all action maps.
        /// </summary>
        public void ClearContexts()
        {
            EnsureNotDisposed();
            m_contextStack.Clear();
            RefreshEnabledMaps();
        }

        /// <summary>
        /// Returns a button stream for currently active context map.
        /// </summary>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive button stream.</returns>
        public IOnityObservable<Unit> GetButtonObservable(string actionName)
        {
            return TryGetByActiveContext(actionName, m_buttonSubjects);
        }

        /// <summary>
        /// Returns a vector stream for currently active context map.
        /// </summary>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive vector stream.</returns>
        public IOnityObservable<Vector2> GetVector2Observable(string actionName)
        {
            return TryGetByActiveContext(actionName, m_vector2Subjects);
        }

        /// <summary>
        /// Returns a float stream for currently active context map.
        /// </summary>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive float stream.</returns>
        public IOnityObservable<float> GetFloatObservable(string actionName)
        {
            return TryGetByActiveContext(actionName, m_floatSubjects);
        }

        /// <summary>
        /// Returns a long-press stream for currently active context map.
        /// </summary>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive long-press stream.</returns>
        public IOnityObservable<Unit> GetLongPressObservable(string actionName)
        {
            return TryGetByActiveContext(actionName, m_longPressSubjects);
        }

        /// <summary>
        /// Returns a long-press progress stream for currently active context map.
        /// </summary>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive long-press progress stream.</returns>
        public IOnityObservable<float> GetLongPressProgressObservable(string actionName)
        {
            return TryGetByActiveContext(actionName, m_longPressProgressSubjects);
        }

        /// <summary>
        /// Returns a button stream for an explicit action map.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive button stream.</returns>
        public IOnityObservable<Unit> GetButtonObservable(string actionMapName, string actionName)
        {
            OnityInputActionKey key = new OnityInputActionKey(actionMapName, actionName);
            return m_buttonSubjects.TryGetValue(key, out Subject<Unit> stream) ? stream : OnityObservable.Empty<Unit>();
        }

        /// <summary>
        /// Returns a vector stream for an explicit action map.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive vector stream.</returns>
        public IOnityObservable<Vector2> GetVector2Observable(string actionMapName, string actionName)
        {
            OnityInputActionKey key = new OnityInputActionKey(actionMapName, actionName);
            return m_vector2Subjects.TryGetValue(key, out Subject<Vector2> stream)
                ? stream
                : OnityObservable.Empty<Vector2>();
        }

        /// <summary>
        /// Returns a float stream for an explicit action map.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        /// <param name="actionName">Action name.</param>
        /// <returns>Reactive float stream.</returns>
        public IOnityObservable<float> GetFloatObservable(string actionMapName, string actionName)
        {
            OnityInputActionKey key = new OnityInputActionKey(actionMapName, actionName);
            return m_floatSubjects.TryGetValue(key, out Subject<float> stream)
                ? stream
                : OnityObservable.Empty<float>();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_updateSubscription.Dispose();

            for (int i = 0; i < m_boundActions.Count; i++)
            {
                InputAction action = m_boundActions[i];
                action.started -= OnActionStarted;
                action.performed -= OnActionPerformed;
                action.canceled -= OnActionCanceled;
            }

            DisposeSubjectMap(m_buttonSubjects);
            DisposeSubjectMap(m_vector2Subjects);
            DisposeSubjectMap(m_floatSubjects);
            DisposeSubjectMap(m_longPressSubjects);
            DisposeSubjectMap(m_longPressProgressSubjects);

            m_boundActions.Clear();
            m_bindingByActionId.Clear();
            m_contextStack.Clear();
            m_tempMapBuffer.Clear();
            m_longPressTrackingStates.Clear();
        }

        private static void DisposeSubjectMap<T>(Dictionary<OnityInputActionKey, Subject<T>> subjects)
        {
            foreach (KeyValuePair<OnityInputActionKey, Subject<T>> pair in subjects)
            {
                pair.Value.Dispose();
            }

            subjects.Clear();
        }

        private IOnityObservable<TValue> TryGetByActiveContext<TValue>(
            string actionName,
            Dictionary<OnityInputActionKey, Subject<TValue>> map)
        {
            if (m_contextStack.Count == 0)
            {
                return OnityObservable.Empty<TValue>();
            }

            OnityInputActionKey key = new OnityInputActionKey(m_contextStack.Peek().name, actionName);
            return map.TryGetValue(key, out Subject<TValue> stream) ? stream : OnityObservable.Empty<TValue>();
        }

        private void BuildActionBindings()
        {
            ReadOnlyArray<InputActionMap> actionMaps = m_actionAsset.actionMaps;

            for (int mapIndex = 0; mapIndex < actionMaps.Count; mapIndex++)
            {
                InputActionMap actionMap = actionMaps[mapIndex];
                ReadOnlyArray<InputAction> actions = actionMap.actions;

                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    InputAction action = actions[actionIndex];
                    OnityInputActionKey key = new OnityInputActionKey(actionMap.name, action.name);
                    ActionBindingState state = new ActionBindingState(action, m_longPressDurationSeconds);
                    m_bindingByActionId[action.id] = state;
                    m_boundActions.Add(action);

                    if (state.ButtonSubject != null)
                    {
                        m_buttonSubjects[key] = state.ButtonSubject;
                    }

                    if (state.Vector2Subject != null)
                    {
                        m_vector2Subjects[key] = state.Vector2Subject;
                    }

                    if (state.FloatSubject != null)
                    {
                        m_floatSubjects[key] = state.FloatSubject;
                    }

                    if (state.LongPressSubject != null)
                    {
                        m_longPressSubjects[key] = state.LongPressSubject;
                    }

                    if (state.LongPressProgressSubject != null)
                    {
                        m_longPressProgressSubjects[key] = state.LongPressProgressSubject;
                        m_longPressTrackingStates.Add(state);
                    }

                    action.started += OnActionStarted;
                    action.performed += OnActionPerformed;
                    action.canceled += OnActionCanceled;
                }
            }

            RefreshEnabledMaps();
        }

        private void OnActionStarted(InputAction.CallbackContext context)
        {
            if (m_bindingByActionId.TryGetValue(context.action.id, out ActionBindingState state) == false)
            {
                return;
            }

            if (state.LongPressProgressSubject == null)
            {
                return;
            }

            state.IsTrackingLongPress = true;
            state.IsLongPressEmitted = false;
            state.PressStartedAtRealtime = Time.realtimeSinceStartup;
            state.LongPressProgressSubject.OnNext(0f);
        }

        private void OnActionPerformed(InputAction.CallbackContext context)
        {
            if (m_bindingByActionId.TryGetValue(context.action.id, out ActionBindingState state) == false)
            {
                return;
            }

            state.ButtonSubject?.OnNext(Unit.Default);

            if (state.Vector2Subject != null)
            {
                state.Vector2Subject.OnNext(context.ReadValue<Vector2>());
            }

            if (state.FloatSubject != null)
            {
                state.FloatSubject.OnNext(context.ReadValue<float>());
            }
        }

        private void OnActionCanceled(InputAction.CallbackContext context)
        {
            if (m_bindingByActionId.TryGetValue(context.action.id, out ActionBindingState state) == false)
            {
                return;
            }

            state.Vector2Subject?.OnNext(Vector2.zero);
            state.FloatSubject?.OnNext(0f);

            if (state.LongPressProgressSubject == null || state.IsTrackingLongPress == false)
            {
                return;
            }

            if (state.IsLongPressEmitted == false)
            {
                state.LongPressProgressSubject.OnNext(-1f);
            }

            state.IsTrackingLongPress = false;
        }

        private void TickLongPressTracking()
        {
            for (int i = 0; i < m_longPressTrackingStates.Count; i++)
            {
                ActionBindingState state = m_longPressTrackingStates[i];

                if (state.IsTrackingLongPress == false)
                {
                    continue;
                }

                float elapsed = Time.realtimeSinceStartup - state.PressStartedAtRealtime;
                float progress = Mathf.Clamp01(elapsed / state.LongPressDurationSeconds);
                state.LongPressProgressSubject.OnNext(progress);

                if (progress < 1f || state.IsLongPressEmitted)
                {
                    continue;
                }

                state.LongPressSubject?.OnNext(Unit.Default);
                state.IsLongPressEmitted = true;
            }
        }

        private void RemoveContextIfPresent(InputActionMap targetMap)
        {
            if (m_contextStack.Contains(targetMap) == false)
            {
                return;
            }

            m_tempMapBuffer.Clear();

            while (m_contextStack.Count > 0)
            {
                InputActionMap currentMap = m_contextStack.Pop();

                if (ReferenceEquals(currentMap, targetMap))
                {
                    continue;
                }

                m_tempMapBuffer.Add(currentMap);
            }

            for (int i = m_tempMapBuffer.Count - 1; i >= 0; i--)
            {
                m_contextStack.Push(m_tempMapBuffer[i]);
            }

            m_tempMapBuffer.Clear();
        }

        private InputActionMap FindActionMap(string actionMapName)
        {
            if (string.IsNullOrWhiteSpace(actionMapName))
            {
                throw new ArgumentException("Action map name cannot be empty.", nameof(actionMapName));
            }

            InputActionMap actionMap = m_actionAsset.FindActionMap(actionMapName, false);

            if (actionMap == null)
            {
                throw new ArgumentException($"Action map '{actionMapName}' does not exist.", nameof(actionMapName));
            }

            return actionMap;
        }

        private void RefreshEnabledMaps()
        {
            ReadOnlyArray<InputActionMap> actionMaps = m_actionAsset.actionMaps;

            for (int i = 0; i < actionMaps.Count; i++)
            {
                actionMaps[i].Disable();
            }

            if (m_contextStack.Count > 0)
            {
                m_contextStack.Peek().Enable();
            }
        }

        private void EnsureNotDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnityReactiveInputPlayer));
            }
        }

        private sealed class ActionBindingState
        {
            public readonly Subject<Unit> ButtonSubject;
            public readonly Subject<Vector2> Vector2Subject;
            public readonly Subject<float> FloatSubject;
            public readonly Subject<Unit> LongPressSubject;
            public readonly Subject<float> LongPressProgressSubject;
            public readonly float LongPressDurationSeconds;
            public bool IsTrackingLongPress;
            public bool IsLongPressEmitted;
            public float PressStartedAtRealtime;

            public ActionBindingState(InputAction action, float longPressDurationSeconds)
            {
                string expectedControlType = action.expectedControlType ?? string.Empty;
                bool isButtonLike =
                    action.type == InputActionType.Button
                    || string.Equals(expectedControlType, "Button", StringComparison.OrdinalIgnoreCase);
                bool isVector2Value =
                    string.Equals(expectedControlType, "Vector2", StringComparison.OrdinalIgnoreCase);
                bool isFloatValue =
                    string.Equals(expectedControlType, "Axis", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expectedControlType, "Analog", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expectedControlType, "Integer", StringComparison.OrdinalIgnoreCase);

                ButtonSubject = isButtonLike ? new Subject<Unit>() : null;
                Vector2Subject = isVector2Value ? new Subject<Vector2>() : null;
                FloatSubject = isFloatValue ? new Subject<float>() : null;
                LongPressSubject = isButtonLike ? new Subject<Unit>() : null;
                LongPressProgressSubject = isButtonLike ? new Subject<float>() : null;
                LongPressDurationSeconds = longPressDurationSeconds;
                IsTrackingLongPress = false;
                IsLongPressEmitted = false;
                PressStartedAtRealtime = 0f;
            }
        }
    }
}
#endif
