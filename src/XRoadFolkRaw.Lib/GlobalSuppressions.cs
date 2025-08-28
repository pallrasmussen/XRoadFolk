// This file is used by Code Analysis to maintain SuppressMessage attributes that are applied to this project.
// To add a suppression to this file, right-click the message in the Error List, point to "Suppress Message", and click
// "In Project Suppression File". You do not need to add suppressions to this file manually.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Public API namespace kept for backward compatibility across solutions; 'Lib' is not a C# keyword and VB interop is not a target.",
    Scope = "namespace",
    Target = "~N:XRoadFolkRaw.Lib.Options")]

[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Public API namespace kept for backward compatibility across solutions; 'Lib' is not a C# keyword and VB interop is not a target.",
    Scope = "namespace",
    Target = "~N:XRoadFolkRaw.Lib")]

[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Public API namespace kept for backward compatibility across solutions; 'Lib' is not a C# keyword and VB interop is not a target.",
    Scope = "namespace",
    Target = "~N:XRoadFolkRaw.Lib.Logging")]