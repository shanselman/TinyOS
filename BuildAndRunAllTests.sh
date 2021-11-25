#!/bin/bash
set -v
pushd .
cd src/TinyOSCore
dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true -p:SuppressTrimAnalysisWarnings=true -p:EnableCompressionInSingleFile=true
cp ../../Sample\ Programs/* bin/Debug/net6.0/linux-x64/publish                    
pushd .                                                     
cd bin/Debug/net6.0/linux-x64/publish
./testAll.sh                                            
popd                                                        
popd
