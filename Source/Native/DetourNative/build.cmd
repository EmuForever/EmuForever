@echo off
rem Builds DetourNative.dll (x64) from upstream recastnavigation Detour 1.6.0 and the C API shim.
rem Output: Source\Native\bin\DetourNative.dll, which Framework.csproj copies next to the server.
setlocal
set HERE=%~dp0
set OUT=%HERE%..\bin
set OBJ=%HERE%obj

rem "(x86)" cannot appear inside a for-in list, so resolve vswhere's path first.
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not defined VCINSTALLDIR (
    for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%i"
)
if not defined VCINSTALLDIR (
    if not defined VSROOT ( echo Visual Studio C++ tools not found. & exit /b 1 )
    call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OBJ%" mkdir "%OBJ%"

cl /nologo /O2 /Oi /GL /EHsc /MT /W3 /LD /DNDEBUG /DDT_POLYREF64 /DDT_VIRTUAL_QUERYFILTER ^
   /I "%HERE%Detour\Include" ^
   "%HERE%Detour\Source\DetourAlloc.cpp" "%HERE%Detour\Source\DetourAssert.cpp" "%HERE%Detour\Source\DetourCommon.cpp" ^
   "%HERE%Detour\Source\DetourNavMesh.cpp" "%HERE%Detour\Source\DetourNavMeshQuery.cpp" "%HERE%Detour\Source\DetourNode.cpp" ^
   "%HERE%DetourNative.cpp" ^
   /Fo"%OBJ%\\" /Fe"%OUT%\DetourNative.dll" /link /LTCG /NOLOGO || exit /b 1

echo Built %OUT%\DetourNative.dll
