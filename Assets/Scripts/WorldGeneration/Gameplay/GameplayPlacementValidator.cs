public static class GameplayPlacementValidator
{
    public static bool Validate(WorldData data)
    {
        return WorldValidator.ValidateGameplay(data);
    }
}
