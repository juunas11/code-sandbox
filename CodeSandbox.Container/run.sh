#!/bin/sh
wget $1 -O ./Program.cs

dotnet build

dotnet run