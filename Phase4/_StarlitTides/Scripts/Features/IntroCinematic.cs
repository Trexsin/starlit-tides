// EXTRA FEATURE 6 - Day-to-Night Intro Cinematic
//
// Plays the intro Timeline (IntroTimeline.playable) before the game starts:
//   1. Sunset over the mountains
//   2. The village lights up
//   3. The bridge over the lagoon
//   4. The forest and the lighthouse
//   5. The sea starts to glow, then the camera settles behind the boat
// The Timeline has a Cinemachine track (the camera shots, with smooth blends
// where clips overlap) and an Animation track that turns the sunset into night
// (SkyController.sunset 1 -> 0) and wakes up the plankton (OceanController.nightGlow 0 -> 1).
// Press Space, Enter or Esc to skip. When it ends, the run starts.

using UnityEngine;
using UnityEngine.Playables;

namespace StarlitTides
{
    public class IntroCinematic : MonoBehaviour
    {
        public static IntroCinematic Instance { get; private set; }

        public PlayableDirector director;
        [Tooltip("Play the intro as soon as the scene starts. (The main menu will turn this off in Phase 6.)")]
        public bool playOnStart = true;

        public bool IsPlaying { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            if (director != null) director.stopped += HandleStopped;
        }

        void OnDisable()
        {
            if (director != null) director.stopped -= HandleStopped;
        }

        void Start()
        {
            if (playOnStart) Play();
        }

        public void Play()
        {
            if (director == null)
            {
                StartGame();
                return;
            }

            if (GameManager.Instance != null) GameManager.Instance.SetState(GameState.Intro);
            IsPlaying = true;
            director.time = 0;
            director.Play();
        }

        void Update()
        {
            if (!IsPlaying) return;
            if (StarlitInput.Instance != null && StarlitInput.Instance.SkipPressed) director.Stop();
        }

        // Called by the director when the Timeline finishes or is skipped.
        void HandleStopped(PlayableDirector playable)
        {
            if (!IsPlaying) return;
            StartGame();
        }

        void StartGame()
        {
            IsPlaying = false;
            if (GameManager.Instance != null) GameManager.Instance.StartRun();
        }
    }
}
