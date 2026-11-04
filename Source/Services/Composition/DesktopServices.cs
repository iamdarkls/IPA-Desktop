namespace IpaDesktop;

/// <summary>Constructor-only composition input. Features receive individual contracts, never this container.</summary>
public sealed record DesktopServices(
    ISettingsStore Settings, WorkspacePaths Paths, IAccountSession Accounts, IAccountProtection Protection,
    IAppleLoginService Login, IAppToolClient Tools, IAppCatalog Catalog, IAppVersions Versions,
    IPurchaseHistory Purchases, IApplicationDownloads Downloads, ILocalApps LocalApps, IAppSets AppSets,
    IDeviceService Devices, IAppleSupport AppleSupport, INetworkDiagnostics Network,
    IToolUpdates ToolUpdates, IGuiUpdates GuiUpdates, IApplicationEvents Events);
