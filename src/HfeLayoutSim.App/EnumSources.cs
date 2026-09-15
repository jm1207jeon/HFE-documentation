using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App;

/// <summary>Static option lists the property panel binds to with {x:Static}.</summary>
public static class EnumSources
{
    public static IReadOnlyList<EnumOption<SemanticRole>> Roles => EnumCatalog.Roles;

    public static IReadOnlyList<EnumOption<InputKind>> InputKinds => EnumCatalog.InputKinds;

    public static IReadOnlyList<EnumOption<ButtonKind>> ButtonKinds => EnumCatalog.ButtonKinds;

    public static IReadOnlyList<EnumOption<string?>> Alignments => EnumCatalog.Alignments;
}
