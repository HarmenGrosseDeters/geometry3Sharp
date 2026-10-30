$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dll = Join-Path $root 'bin\Release\netstandard2.0\geometry3Sharp.dll'
if (!(Test-Path $dll)) { throw 'Build the netstandard2.0 library before running this regression.' }
$source = Get-Content (Join-Path $PSScriptRoot 'Program.cs') -Raw
$match = [regex]::Match($source, '(?ms)^\s*bool valid = true;\s*\r?\n\s*try\s*\{.*?\r?\n\s*r\["after_valid"\] = valid;')
if (!$match.Success -or [regex]::Matches($source, 'r\["after_valid"\] = valid;').Count -ne 1) {
    throw 'Actual-result reporting adapter changed; update the extraction deliberately.'
}
# Compile the exact current adapter block, not a handwritten reproduction. This is not an end-to-end repair test.
$typeName = 'ValidityReportingProbe' + [Guid]::NewGuid().ToString('N')
$code = @"
using System;
using System.Collections.Generic;
using g3;
public static class $typeName {
    public static bool Report(DMesh3 result) {
        var r = new Dictionary<string, object>();
        $($match.Value)
        return (bool)r["after_valid"];
    }
    public static void Check() {
        var mesh = new DMesh3();
        foreach (var p in new[] { Vector3d.Zero, new Vector3d(1,0,0), new Vector3d(0,1,0),
            new Vector3d(-1,0,0), new Vector3d(0,-1,0) }) mesh.AppendVertex(p);
        if (mesh.AppendTriangle(0,1,2) < 0 || mesh.AppendTriangle(0,3,4) < 0)
            throw new Exception("Invalid procedural fixture construction");
        if (!mesh.CheckValidity(true, FailMode.ReturnOnly) || !mesh.IsBowtieVertex(0))
            throw new Exception("Expected an otherwise structurally valid bowtie");
        bool actual = mesh.CheckValidity(false, FailMode.ReturnOnly);
        bool reported = Report(mesh);
        Console.WriteLine("source-adapter: actual validity={0}; after_valid={1}", actual, reported);
        if (actual) throw new Exception("Fixture did not exercise false-without-exception validity");
        if (reported != actual) throw new Exception("REGRESSION: after_valid ignores the false CheckValidity return");
        var validMesh = new TrivialBox3Generator().Generate().MakeDMesh();
        if (!validMesh.CheckValidity(false, FailMode.ReturnOnly) || !Report(validMesh))
            throw new Exception("REGRESSION: valid mesh must be reported as valid");
        if (Report(null)) throw new Exception("REGRESSION: validity exceptions must be reported as invalid");
        Console.WriteLine("source-adapter: valid mesh=True; exception=False");
    }
}
"@
$facade = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'
if (!(Test-Path $facade)) {
    $facade = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\netstandard.dll'
}
if (!(Test-Path $facade)) { throw 'The .NET Framework 4.8 netstandard reference facade is unavailable.' }
Add-Type -Path $dll
$runtime = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
Add-Type -TypeDefinition $code -ReferencedAssemblies @(
    [object].Assembly.Location, $dll, $facade, (Join-Path $runtime 'System.dll'), (Join-Path $runtime 'System.Core.dll'))
$type = $typeName -as [type]
$type.GetMethod('Check').Invoke($null, @())
