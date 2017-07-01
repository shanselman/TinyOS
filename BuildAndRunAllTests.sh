#!/bin/bash
set -v
pushd .
cd OS\ Project/
dotnet build -c Debug                              
cp ../Sample\ Programs/* bin/Debug/                       
pushd .                                                     
cd bin/Debug/                                                
./testAll.sh                                            
popd                                                        
popd
