using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Magellan 3")]
[assembly: AssemblyDescription("Places database, dungeon identification, and live dungeon automap for Decal 3 / End-of-Retail Asheron's Call. A 2026 rebuild of Adam Wright's Magellan 2 (2003).")]
[assembly: AssemblyProduct("Magellan 3")]

[assembly: ComVisible(true)]
[assembly: Guid("7C4B2A18-3E6D-4F91-B2A7-9C1E5D3F8A02")]

// AssemblyVersion stays FIXED: the COM registration (RegAsm) records the assembly's full name
// INCLUDING this version, so bumping it forces every user to re-register. Release identity lives
// in AssemblyFileVersion / the version constant in PluginCore instead.
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]
[assembly: AssemblyInformationalVersion("1.2.2")]
