using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OnityShowcase
{
    /// <summary>
    /// Thin player view. Reads movement from the new Input System (Unity 6 default) and moves the
    /// player transform — pure input-to-transform forwarding with no game logic and no service
    /// dependency, so it needs no container injection. Coins are collected by clicking them; the
    /// player marker simply gives the scene something to drive while the round runs. Reads devices
    /// directly so the showcase needs no .inputactions asset wiring.
    /// </summary>
    public sealed class PlayerBehaviour : MonoBehaviour
    {
        [Tooltip("Movement speed in world units per second.")]
        [SerializeField] private float m_speed = 6f;

        private void Update()
        {
            Vector2 input = ReadMoveInput();

            if (input.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 delta = new Vector3(input.x, 0f, input.y) * (m_speed * Time.deltaTime);
            transform.Translate(delta, Space.World);
        }

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 move = Vector2.zero;
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    move.x -= 1f;
                }

                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    move.x += 1f;
                }

                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                {
                    move.y -= 1f;
                }

                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                {
                    move.y += 1f;
                }
            }

            Gamepad gamepad = Gamepad.current;

            if (gamepad != null)
            {
                move += gamepad.leftStick.ReadValue();
            }

            return Vector2.ClampMagnitude(move, 1f);
#else
            // Input System is the project default; legacy input is intentionally not implemented.
            return Vector2.zero;
#endif
        }
    }
}
