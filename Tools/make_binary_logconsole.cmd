@echo off
@REM Until new major version of C# is released the build number in the path will keep counting.
"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" ..\Source\KSP2Dev_LogConsole.csproj /t:Clean,Build /p:Configuration=Release
