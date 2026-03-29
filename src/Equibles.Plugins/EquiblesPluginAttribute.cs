namespace Equibles.Plugins;

/// <summary>
/// Marks a third-party assembly as an Equibles plugin.
/// Assemblies matching the Equibles.*.dll naming convention are loaded automatically.
/// Use this attribute for non-Equibles assemblies that provide plugin functionality.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EquiblesPluginAttribute : Attribute;
