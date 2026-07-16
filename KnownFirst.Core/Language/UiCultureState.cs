namespace KnownFirst.Core.Language;

public sealed record UiCultureState(
    string CurrentCulture,
    string CurrentUiCulture,
    string DefaultThreadCurrentCulture,
    string DefaultThreadCurrentUiCulture);
