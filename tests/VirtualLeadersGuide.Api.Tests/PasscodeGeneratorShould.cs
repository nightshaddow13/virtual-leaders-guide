using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

public class PasscodeGeneratorShould
{
    [Fact]
    public void ProduceANonEmptyValue_ForGenerate()
    {
        Assert.False(string.IsNullOrWhiteSpace(PasscodeGenerator.Generate()));
    }

    [Fact]
    public void ProduceOnlyLetters_ForGenerate()
    {
        string passcode = PasscodeGenerator.Generate();

        Assert.All(passcode, c => Assert.True(char.IsLetter(c)));
    }

    [Fact]
    public void ProduceDifferentValues_AcrossRepeatedCalls_ForGenerate()
    {
        var generated = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            generated.Add(PasscodeGenerator.Generate());
        }

        Assert.True(generated.Count > 1);
    }
}
