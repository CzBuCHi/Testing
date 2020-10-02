@ECHO OFF
SET GIT_MERGE_FILE="c:\\Program Files\Git\mingw64\libexec\git-core\git-merge-file.exe"
SET TORTOISE="c:\Program Files\TortoiseGit\bin\TortoiseGitMerge.exe"
SET MINE="c:\projects\Testing\test\Mine\test.txt"
SET BASE="c:\projects\Testing\test\Base\test.txt"
SET THEIRS="c:\projects\Testing\test\Server\test.txt"

%GIT_MERGE_FILE% %MINE% %BASE% %THEIRS% -p -q > NUL

IF ERRORLEVEL 1 (
    %TORTOISE% %BASE% %MINE% %THEIRS%    
) ELSE (   
    %GIT_MERGE_FILE% %MINE% %BASE% %THEIRS% -q
)

:end
ECHO.

