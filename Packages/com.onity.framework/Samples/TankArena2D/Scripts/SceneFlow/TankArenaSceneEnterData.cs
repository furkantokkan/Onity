using Onity.Unity.SceneFlow;

namespace Onity.Samples.TankArena2D.SceneFlow
{
    /// <summary>
    /// Identifies where a scene transition request originated.
    /// </summary>
    public enum TankArenaSceneEntrySource
    {
        /// <summary>
        /// Entry was requested by bootstrap flow.
        /// </summary>
        Bootstrap = 0,

        /// <summary>
        /// Entry was requested by main menu flow.
        /// </summary>
        MainMenu = 1,

        /// <summary>
        /// Entry was requested by gameplay flow.
        /// </summary>
        Gameplay = 2
    }

    /// <summary>
    /// Entry payload for Main Menu scene.
    /// </summary>
    public sealed class TankArenaMainMenuEnterData : IOnitySceneEnterData
    {
        /// <summary>
        /// Initializes Main Menu entry payload.
        /// </summary>
        /// <param name="entrySource">Transition source.</param>
        /// <param name="lastScore">Last gameplay score snapshot.</param>
        /// <param name="lastWave">Last gameplay wave snapshot.</param>
        public TankArenaMainMenuEnterData(
            TankArenaSceneEntrySource entrySource,
            int lastScore,
            int lastWave)
        {
            EntrySource = entrySource;
            LastScore = lastScore;
            LastWave = lastWave;
        }

        /// <summary>
        /// Transition source.
        /// </summary>
        public TankArenaSceneEntrySource EntrySource { get; }

        /// <summary>
        /// Score snapshot from previous scene.
        /// </summary>
        public int LastScore { get; }

        /// <summary>
        /// Wave snapshot from previous scene.
        /// </summary>
        public int LastWave { get; }
    }

    /// <summary>
    /// Entry payload for Gameplay scene.
    /// </summary>
    public sealed class TankArenaGameplayEnterData : IOnitySceneEnterData
    {
        /// <summary>
        /// Initializes Gameplay entry payload.
        /// </summary>
        /// <param name="sessionSeed">Deterministic session seed.</param>
        /// <param name="startingWave">First wave index for the session.</param>
        /// <param name="enemyBonusPerWave">Additional enemies per wave for this session.</param>
        public TankArenaGameplayEnterData(
            int sessionSeed,
            int startingWave,
            int enemyBonusPerWave)
        {
            SessionSeed = sessionSeed;
            StartingWave = startingWave;
            EnemyBonusPerWave = enemyBonusPerWave;
        }

        /// <summary>
        /// Deterministic session seed.
        /// </summary>
        public int SessionSeed { get; }

        /// <summary>
        /// First wave index for the session.
        /// </summary>
        public int StartingWave { get; }

        /// <summary>
        /// Additional enemies per wave applied for this session.
        /// </summary>
        public int EnemyBonusPerWave { get; }
    }
}
