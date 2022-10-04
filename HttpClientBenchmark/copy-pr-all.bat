echo "Removing existing folder"
del /S /Q ..\pr\*

echo "Copying coreclr"
robocopy C:\MihaZupan\runtime\artifacts\bin\coreclr\windows.x64.Release ..\pr /E /NFL

echo "Copying runtime"
robocopy C:\MihaZupan\runtime\artifacts\bin\runtime\net7.0-windows-Release-x64 ..\pr /E /NFL