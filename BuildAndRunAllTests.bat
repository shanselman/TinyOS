pushd .
cd "src\TinyOSCore"
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:SuppressTrimAnalysisWarnings=true -p:EnableCompressionInSingleFile=true
copy "..\..\Sample Programs\*" bin\Debug\net6.0\win-x64\publish
pushd .
cd bin\Debug\net6.0\win-x64\publish
call testAll.bat
popd
popd
