#!/bin/sh
# Builds Apocatremors.dll against the game's own libraries. Usage: ./build.sh [out.dll]
M=${MANAGED:-/e/SteamLibrary/steamapps/common/Apocalypter/Apocalypter_Data/Managed}; B=${BEPCORE:-/e/SteamLibrary/steamapps/common/Apocalypter/BepInEx/core}
mcs -nostdlib -noconfig -target:library -langversion:latest -optimize+ -out:${1:-Apocatremors.dll} \
  -r:$M/mscorlib.dll -r:$M/System.dll -r:$M/System.Core.dll -r:$M/netstandard.dll \
  -r:$B/BepInEx.dll \
  -r:$M/UnityEngine.dll -r:$M/UnityEngine.CoreModule.dll -r:$M/UnityEngine.PhysicsModule.dll \
  -r:$M/UnityEngine.TerrainModule.dll -r:$M/UnityEngine.TerrainPhysicsModule.dll -r:$M/UnityEngine.ParticleSystemModule.dll \
  -r:$M/UnityEngine.UI.dll -r:$M/UnityEngine.UIModule.dll -r:$M/UnityEngine.TextRenderingModule.dll -r:$M/Unity.InputSystem.dll -r:$M/PlayMaker.dll -r:$M/Assembly-CSharp.dll \
  Plugin.cs Catalog.cs Tremors.cs Notice.cs
