namespace Data.BuildFacts;

/// <summary>
/// A participant's end-of-game inventory as every build derivation reads it: the six
/// inventory slots plus Riot's role-bound slot, which holds a bot laner's boots once
/// the role quest is done (six items and boots) and the other roles' quest reward.
/// The trinket slot (<c>item6</c>) is never part of it — wards and lenses are not
/// build items, and keeping them out structurally beats filtering them by id.
/// </summary>
public static class FinalInventory
{
    public static int[] Of(
        int item0, int item1, int item2, int item3, int item4, int item5, int roleBoundItemId)
        => [item0, item1, item2, item3, item4, item5, roleBoundItemId];
}
