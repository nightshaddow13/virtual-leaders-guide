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

        // Not a strict guarantee (two random two-word phrases could collide), but with the EFF Large
        // Wordlist's ~7,776 words, 20 calls colliding even once is astronomically unlikely - this is really
        // asserting "Generate() isn't returning a constant," not measuring true randomness.
        Assert.True(generated.Count > 1);
    }
}
