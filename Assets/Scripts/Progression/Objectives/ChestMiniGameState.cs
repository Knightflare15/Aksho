public static class ChestMiniGameState
{
    public static bool IsOpen =>
        ChestCountingMiniGameUI.IsOpen ||
        ChestColorMiniGameUI.IsOpen;
}

