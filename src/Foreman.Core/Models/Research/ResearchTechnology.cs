namespace Foreman.Models.Research {
    public readonly record struct ResearchTechnology(string Name, bool Researched, int LevelsDone, int MinLevel, int MaxLevel) {
        public const int Unlimited = int.MaxValue;

        public bool IsUnlimited => MaxLevel == Unlimited;
        public int LevelCount => IsUnlimited ? Unlimited : MaxLevel - MinLevel + 1;
        public bool IsLeveled => IsUnlimited || LevelCount > 1;
        public bool IsPartlyResearched => !Researched && IsLeveled && LevelsDone > 0;
    }
}
