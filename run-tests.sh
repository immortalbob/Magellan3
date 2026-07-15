#!/usr/bin/env bash
# Runs the pure-logic test suite. No NuGet feed required (uses the SDK's on-disk packs).
# On a machine with a NuGet feed, plain `dotnet run` from the test dir also works.
set -euo pipefail
cd "$(dirname "$0")"

# Compile Core + tests together with no external packages, so this works fully offline.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cat > "$work/runner.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Magellan</RootNamespace>
    <AssemblyName>runner</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$PWD/src/Magellan3.Core/**/*.cs" />
    <Compile Include="$PWD/tests/Magellan3.Tests/**/*.cs" />
  </ItemGroup>
</Project>
PROJ
cat > "$work/nuget.config" <<NUGET
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>
NUGET
# data/ is found by the harness walking up from the binary; symlink it next to the runner.
ln -s "$PWD/data" "$work/data"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build "$work/runner.csproj" -c Release --disable-build-servers -v q
dotnet "$work/bin/Release/net8.0/runner.dll"
