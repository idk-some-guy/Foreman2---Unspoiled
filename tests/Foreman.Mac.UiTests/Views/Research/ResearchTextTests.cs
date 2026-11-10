using Foreman.Mac.Views.Research;
using Foreman.Models.Research;
using System;
using Xunit;

namespace Foreman.Mac.UiTests.Views.Research {
    public class ResearchTextTests {
        [Theory]
        [InlineData(0.3, "30 %")]
        [InlineData(1.5, "150 %")]
        [InlineData(0.125, "12.5 %")]
        [InlineData(0.0, "0 %")]
        public void Percent_ShowsTheFractionTimesOneHundred(double fraction, string expected) =>
            Assert.Equal(expected, ResearchText.Percent(fraction));

        [Theory]
        [InlineData(2.0, "+2")]
        [InlineData(0.0, "+0")]
        public void Count_ShowsAPlusSign(double value, string expected) =>
            Assert.Equal(expected, ResearchText.Count(value));

        [Fact]
        public void Value_UsesCountOrPercentPerField_AndNoneForAbsent() {
            Assert.Equal("120 %", ResearchText.Value(ResearchField.MiningProductivity, 1.2));
            Assert.Equal("+3", ResearchText.Value(ResearchField.InserterStackBonus, 3));
            Assert.Equal("none", ResearchText.Value(ResearchField.LabSpeed, null));
        }

        [Fact]
        public void PlayTime_FloorsTicksToMinutes_AndDropsWhenUnknown() {
            Assert.Equal("41 h 7 min", ResearchText.PlayTime(8_881_200));
            Assert.Null(ResearchText.PlayTime(null));
            Assert.Equal("41 hours 7 minutes", ResearchText.PlayTimeSpoken(8_881_200));
            Assert.Null(ResearchText.PlayTimeSpoken(null));
        }

        [Fact]
        public void SyncedWhen_NamesTodayYesterdayAndTheDate() {
            DateTime now = new(2026, 11, 10, 18, 0, 0);
            Assert.Equal("today at 14:05", ResearchText.SyncedWhen(Utc(new DateTime(2026, 11, 10, 14, 5, 0)), now));
            Assert.Equal("yesterday at 9:12", ResearchText.SyncedWhen(Utc(new DateTime(2026, 11, 9, 9, 12, 0)), now));
            Assert.Equal("Nov 2 at 9:12", ResearchText.SyncedWhen(Utc(new DateTime(2026, 11, 2, 9, 12, 0)), now));
        }

        [Theory]
        [InlineData(1, false, "one")]
        [InlineData(1, true, "One")]
        [InlineData(4, false, "four")]
        [InlineData(9, true, "Nine")]
        [InlineData(10, false, "10")]
        [InlineData(10, true, "10")]
        public void CountWords_SpellsUnderTenAndCapitalisesAtASentenceStart(int n, bool sentenceStart, string expected) =>
            Assert.Equal(expected, ResearchText.CountWords(n, sentenceStart));

        [Fact]
        public void FieldLabel_NamesEveryField() {
            foreach (ResearchField field in ResearchFields.All)
                Assert.False(string.IsNullOrWhiteSpace(ResearchText.FieldLabel(field)));
            Assert.Equal("Braking force", ResearchText.FieldLabel(ResearchField.TrainBrakingForce));
        }

        private static DateTime Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
    }
}
