#!/bin/bash
# pass verision number as first argument
set packageVersion=$1
echo "Version number: $packageVersion"
# set nuget API key
export NUGET_API_KEY=oy2ecvf4d7qzb6jvfgtaoovta6rkynderpuuqnhpo4ew2q
# check if version number is not set then ask for it
if [ -z "$packageVersion" ]; then
  echo "Enter version number:"
  read packageVersion
fi

# Build the TabTabGo.Templating
dotnet build ./src/TabTabGo.Templating/TabTabGo.Templating.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Templating
dotnet nuget push ./src/TabTabGo.Templating/bin/Release/TabTabGo.Templating.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

# build the TabTabGo.Templating.Dotliquid
dotnet build ./src/TabTabGo.Templating.Dotliquid/TabTabGo.Templating.Dotliquid.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Templating.Dotliquid
dotnet nuget push ./src/TabTabGo.Templating.Dotliquid/bin/Release/TabTabGo.Templating.Dotliquid.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY

#build the TabTabGo.Templating.OpenXml
dotnet build ./src/TabTabGo.Templating.OpenXml/TabTabGo.Templating.OpenXml.csproj -c Release --version-suffix $packageVersion
# Publish the TabTabGo.Templating.OpenXml
dotnet nuget push ./src/TabTabGo.Templating.OpenXml/bin/Release/TabTabGo.Templating.OpenXml.$packageVersion.nupkg -s https://api.nuget.org/v3/index.json -k $NUGET_API_KEY