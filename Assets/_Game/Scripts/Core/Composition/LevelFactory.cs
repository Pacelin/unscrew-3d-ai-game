namespace Unscrew.Core
{
    /// <summary>
    /// Composition root of one played level: the only place in Core that knows concrete modules.
    /// Used by the Unity game layer and by integration tests.
    /// </summary>
    public sealed class LevelFactory
    {
        private readonly IDifficultyCurve _curve;
        private readonly ILayoutGenerator _generator;

        public LevelFactory() : this(new DifficultyCurve(), new LayoutGenerator()) { }

        public LevelFactory(IDifficultyCurve curve, ILayoutGenerator generator)
        {
            _curve = curve;
            _generator = generator;
        }

        /// <summary>
        /// <paramref name="displayNumber"/> is what the player sees (21+ in endless mode);
        /// difficulty and seed follow it, geometry comes from <paramref name="def"/>.
        /// </summary>
        public PreparedLevel Prepare(LevelDefinition def, int displayNumber)
        {
            int sourceNumber = def.number;
            def.number = displayNumber;
            var settings = _curve.Get(def);
            var layout = _generator.Generate(def, settings);
            def.number = sourceNumber;
            return new PreparedLevel(def, settings, layout);
        }

        public static IGameSession CreateSession(PreparedLevel level)
        {
            var s = level.Settings;
            var collector = new Collector(level.Layout.boxQueue, s.activeBoxes, s.boxCapacity, s.bufferSlots);
            return new GameSession(level.Definition, level.Layout, collector);
        }
    }

    public sealed class PreparedLevel
    {
        public readonly LevelDefinition Definition;
        public readonly DifficultySettings Settings;
        public readonly LevelLayout Layout;

        public PreparedLevel(LevelDefinition definition, DifficultySettings settings, LevelLayout layout)
        {
            Definition = definition;
            Settings = settings;
            Layout = layout;
        }
    }
}
