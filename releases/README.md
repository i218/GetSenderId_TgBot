# Releases

Run `./releases/build.ps1 -Tag v<version>` from the repository root. The tag must match the `<Version>` value in `GetSenderId_TgBot/GetSenderId_TgBot.csproj`.

The script creates a self-contained Windows x64 folder containing the executable and its SHA-256 checksum. Generated release folders are intentionally excluded from Git.
