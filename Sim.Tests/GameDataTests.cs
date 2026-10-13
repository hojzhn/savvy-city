using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Tests;

public class GameDataTests
{
    [Fact]
    public void RealDataLoads()
    {
        var data = TestData.Load();
        Assert.NotEmpty(data.Locations);
        Assert.NotEmpty(data.Family);
    }

    [Theory]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: wealth
                values: [poor, rich]
                mods:
                  - when: { household.nonexistent: x }
                    mul: { poor: 2 }
        """, "unknown or not generated yet")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: wealth
                values: [poor, rich]
                mods:
                  - when: { context.urbanicity: moon_base }
                    mul: { poor: 2 }
        """, "has no value 'moon_base'")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: wealth
                values: [poor, rich]
                mods:
                  - when: { context.urbanicity: rural }
                    mul: { middle: 2 }
        """, "'middle', which is not one of its values")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: a
                values: [x, y]
                mods:
                  - when: { household.b: x }
                    mul: { x: 2 }
              - id: b
                values: [x, y]
        """, "not generated yet")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: a
                values: [x, y]
                mods:
                  - when: { year: 1990 }
                    mul: { x: 2 }
        """, "is numeric")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: a
                values: [x, y]
                only_if: { context.urbanicity: rural }
        """, "only_if needs a default")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: a
                values: [x, y]
                mods:
                  - when: { self.education: x }
                    mul: { x: 2 }
        """, "only works inside a per-parent group")]
    [InlineData("""
        groups:
          - name: household
            items:
              - id: a
                values: [x, y]
                typo_field: 3
        """, "family.yaml line")]
    public void BadFamilyTablesAreRejected(string yaml, string expectedMessage)
    {
        var e = Assert.Throws<DataException>(() => TestData.WithFamily(yaml));
        Assert.Contains(expectedMessage, e.Message);
    }

    [Fact]
    public void InvalidCorrelationMatrixIsRejected()
    {
        var config = """
            present_year: 2026
            age_min: 21
            age_max: 45
            male_birth_rate: 0.5
            temperament:
              traits: [threat_sensitivity, reward_drive, self_control]
              correlations:
                - { a: threat_sensitivity, b: self_control, r: 0.9 }
                - { a: threat_sensitivity, b: reward_drive, r: 0.9 }
                - { a: reward_drive, b: self_control, r: -0.9 }
            """;
        var e = Assert.Throws<DataException>(() => GameData.FromYaml(
            config, TestData.ReadFile("locations.yaml"), TestData.ReadFile("eras.yaml"), TestData.ReadFile("family.yaml")));
        Assert.Contains("not a valid correlation matrix", e.Message);
    }
}
