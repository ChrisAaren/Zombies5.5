using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zombies55
{
    public enum RunState { Ready, Running, Paused, Won, Lost }

    // The one owner of run state. Shell, feel, audio and live services subscribe to its events.
    public class GameLoop : MonoBehaviour
    {
        public static GameLoop Instance { get; private set; }

        // Set by Restart() so the reloaded scene skips the title. Consumed in Awake.
        static bool s_StartImmediately;

        [Tooltip("Seconds the player has to collect every token.")]
        [SerializeField] float runSeconds = 90f;

        public RunState State { get; private set; } = RunState.Ready;
        public int TargetCount { get; private set; }
        public int Collected { get; private set; }
        public float TimeLeft { get; private set; }
        public float RunSeconds => runSeconds;
        public float ElapsedSeconds => runSeconds - TimeLeft;

        public event Action<RunState> StateChanged;
        public event Action<Pickup> PickupCollected;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            TimeLeft = runSeconds;
            Collected = 0;
            TargetCount = FindObjectsByType<Pickup>(FindObjectsInactive.Exclude).Length;
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

        void Update()
        {
            if (State != RunState.Running) return;
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                Lose();
            }
        }

        public void StartRun()
        {
            if (State == RunState.Ready) Set(RunState.Running);
        }

        public void Collect(Pickup pickup)
        {
            if (State != RunState.Running) return;
            Collected++;
            PickupCollected?.Invoke(pickup);
            if (Collected >= TargetCount) Win();
        }

        public void Win()
        {
            if (State == RunState.Running) Set(RunState.Won);
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
