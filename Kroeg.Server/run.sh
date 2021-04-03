#!/bin/sh

dotnet CreateDatabase.dll

dotnet Kroeg.Server.dll
