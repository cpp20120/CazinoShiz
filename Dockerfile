FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY CasinoShiz.slnx ./
COPY framework/BotFramework.Sdk/BotFramework.Sdk.csproj         framework/BotFramework.Sdk/
COPY framework/BotFramework.Sdk.Testing/BotFramework.Sdk.Testing.csproj framework/BotFramework.Sdk.Testing/
COPY framework/BotFramework.Host/BotFramework.Host.csproj       framework/BotFramework.Host/
COPY games/Games.Dice/Games.Dice.csproj                         games/Games.Dice/
COPY games/Games.DiceCube/Games.DiceCube.csproj                 games/Games.DiceCube/
COPY games/Games.Darts/Games.Darts.csproj                       games/Games.Darts/
COPY games/Games.Basketball/Games.Basketball.csproj             games/Games.Basketball/
COPY games/Games.Football/Games.Football.csproj                 games/Games.Football/
COPY games/Games.Bowling/Games.Bowling.csproj                   games/Games.Bowling/
COPY games/Games.Challenges/Games.Challenges.csproj             games/Games.Challenges/
COPY games/Games.Blackjack/Games.Blackjack.csproj               games/Games.Blackjack/
COPY games/Games.Horse/Games.Horse.csproj                       games/Games.Horse/
COPY games/Games.Poker/Games.Poker.csproj                       games/Games.Poker/
COPY games/Games.SecretHitler/Games.SecretHitler.csproj         games/Games.SecretHitler/
COPY games/Games.Redeem/Games.Redeem.csproj                     games/Games.Redeem/
COPY games/Games.Leaderboard/Games.Leaderboard.csproj           games/Games.Leaderboard/
COPY games/Games.PixelBattle/Games.PixelBattle.csproj           games/Games.PixelBattle/
COPY games/Games.Transfer/Games.Transfer.csproj                 games/Games.Transfer/
COPY games/Games.Admin/Games.Admin.csproj                       games/Games.Admin/
COPY host/CasinoShiz.Host/CasinoShiz.Host.csproj                host/CasinoShiz.Host/
RUN dotnet restore host/CasinoShiz.Host/CasinoShiz.Host.csproj

COPY framework/ framework/
COPY games/     games/
COPY host/      host/
RUN dotnet publish host/CasinoShiz.Host/CasinoShiz.Host.csproj -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000

ENTRYPOINT ["dotnet", "CasinoShiz.Host.dll"]
