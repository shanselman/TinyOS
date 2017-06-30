msbuild /p:Configuration=Debug
copy ".\Sample Programs\*" bin\Debug
pushd .
cd bin\Debug
call testAll.bat
popd
