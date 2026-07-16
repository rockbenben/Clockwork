using Clockwork.Core;
using Xunit;

public class ActionGroupResolverTests
{
    private static List<ActionGroup> Groups() => new()
    {
        new ActionGroup { Id = "a", Name = "组A" },
        new ActionGroup { Id = "b", Name = "组B" },
    };

    [Fact] public void Resolves_by_id() => Assert.Equal("组B", ActionGroupResolver.Resolve(Groups(), "b")!.Name);
    [Fact] public void Empty_id_null() => Assert.Null(ActionGroupResolver.Resolve(Groups(), ""));
    [Fact] public void Missing_id_null() => Assert.Null(ActionGroupResolver.Resolve(Groups(), "zzz"));
    [Fact] public void Null_list_null() => Assert.Null(ActionGroupResolver.Resolve(null, "a"));
}
