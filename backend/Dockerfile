FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MatchForecast.Api/MatchForecast.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 MATCHFORECAST_LOG_DIR=/app/logs TZ=Europe/Istanbul
ENTRYPOINT ["dotnet", "MatchForecast.Api.dll"]