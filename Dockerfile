FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY src/CasinoShiz/CasinoShiz.csproj src/CasinoShiz/
COPY src/CasinoShiz.Core/CasinoShiz.Core.csproj src/CasinoShiz.Core/
COPY src/CasinoShiz.Data/CasinoShiz.Data.csproj src/CasinoShiz.Data/
RUN dotnet restore src/CasinoShiz/CasinoShiz.csproj
COPY src/CasinoShiz/ src/CasinoShiz/
COPY src/CasinoShiz.Core/ src/CasinoShiz.Core/
COPY src/CasinoShiz.Data/ src/CasinoShiz.Data/
RUN dotnet publish src/CasinoShiz/CasinoShiz.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY src/CasinoShiz.Core/fonts/ /app/fonts/

EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000

ENTRYPOINT ["dotnet", "CasinoShiz.dll"]
