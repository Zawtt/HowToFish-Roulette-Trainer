namespace HowToFish.RouletteTrainer.Bridge;

internal sealed class TargetSelector
{
    private int _blackIndex;
    private int _redIndex;

    internal int Select(ForceColor color)
    {
        switch (color)
        {
            case ForceColor.Green:
                return 0;
            case ForceColor.Black:
                return 1 + 2 * Next(ref _blackIndex);
            case ForceColor.Red:
                return 2 + 2 * Next(ref _redIndex);
            default:
                return -1;
        }
    }

    private static int Next(ref int index)
    {
        // Step seven is coprime to the 18 same-color slots, so every slot is
        // used once before the sequence repeats. This does not consume Unity RNG.
        var result = index * 7 % 18;
        index = (index + 1) % 18;
        return result;
    }
}
