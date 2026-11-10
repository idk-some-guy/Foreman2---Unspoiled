using System;
using System.Collections.Generic;

namespace Foreman.Models.Research {
    public static class ResearchFields {
        public static IReadOnlyList<ResearchField> All { get; } = Enum.GetValues<ResearchField>();

        public static bool IsCount(ResearchField field) => field switch {
            ResearchField.InserterStackBonus => true,
            ResearchField.BulkInserterCapacityBonus => true,
            ResearchField.BeltStackBonus => true,
            ResearchField.RobotCargo => true,
            _ => false,
        };

        public static string ExportKey(ResearchField field) => field switch {
            ResearchField.MiningProductivity => "mining_drill_productivity_bonus",
            ResearchField.LabSpeed => "laboratory_speed_modifier",
            ResearchField.LabProductivity => "laboratory_productivity_bonus",
            ResearchField.InserterStackBonus => "inserter_stack_size_bonus",
            ResearchField.BulkInserterCapacityBonus => "bulk_inserter_capacity_bonus",
            ResearchField.BeltStackBonus => "belt_stack_size_bonus",
            ResearchField.RobotSpeed => "worker_robots_speed_modifier",
            ResearchField.RobotCargo => "worker_robots_storage_bonus",
            ResearchField.RobotBattery => "worker_robots_battery_modifier",
            ResearchField.TrainBrakingForce => "train_braking_force_bonus",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }
}
