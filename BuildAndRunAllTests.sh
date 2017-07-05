#!/bin/bash
set -v
pushd .
cd src/TinyOSCore
dotnet build -c Debug                              
cp ../../Sample\ Programs/* bin/Debug/                       
pushd .                                                     
cd bin/Debug/                                                
./testAll.sh                                            
popd                                                        
popd
