using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zombies55
{
    public enum RunState { Ready, Running, Paused, Lost }

    // The one owner of run state. Shell, feel, audio and live services subscribe to its events.
    public class GameLoop : MonoBehaviour
    {
        public static GameLoop Instance { get; private set; }

        // Set by Restart() so the reloaded scene skips the title. Consumed in Awake.
        static bool s_StartImmediately;

        public RunState State { get; private set; } = RunState.Ready;

        public event Action<RunState> StateChanged;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        // Re-register after a script reload during Play mode, which clears statics.
        void OnEnable()
        {
            Instance = this;
        }

        void Start()
        {
            if (s_StartImmediately)
            {
                s_StartImmediately = false;
                StartRun();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartRun()
        {
            if (State == RunState.Ready) Set(RunState.Running);
        }

        public void Lose()
        {
            if (State == RunState.Running) Set(RunState.Lost);
        }

        public void Pause()
        {
            if (State != RunState.Running) return;
            Time.timeScale = 0f;
            Set(RunState.Paused);
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            Time.timeScale = 1f;
            Set(RunState.Running);
        }

        // Fresh run, straight into play.
        public void Restart()
        {
            s_StartImmediately = true;
            Reload();
        }

        // Fresh run, back to the title screen.
        public void ReturnToTitle()
        {
            s_StartImmediately = false;
            Reload();
        }

        static void Reload()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Set(RunState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
