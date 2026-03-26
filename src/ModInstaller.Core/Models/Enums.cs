namespace ModInstaller.Core.Models;

public enum ModType
{
    Unknown,
    Aircraft,
    Helicopter,
    Scenery,
    Livery,
    Avionics,
    Effects,
    Models,
    Missions,
    Instruments,
    Weather,
    Sounds,
    Other
}

public enum ModStatus
{
    Enabled,
    Disabled
}

public enum SimVersion
{
    Unknown,
    MSFS2020,
    MSFS2024
}

public enum InstallSource
{
    Unknown,
    MSStore,
    Steam,
    Custom
}

public enum LinkType
{
    Copy,
    Junction,
    SymbolicLink
}

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical
}
