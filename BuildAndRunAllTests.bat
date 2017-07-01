pushd .
cd "OS Project"
dotnet build -c DEBUG
copy "..\Sample Programs\*" bin\Debug
pushd .
cd bin\Debug
call testAll.bat
popd
popd
