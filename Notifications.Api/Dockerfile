FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FCG.Messaging.Contracts/FCG.Messaging.Contracts.csproj FCG.Messaging.Contracts/
COPY Notifications.Api/Notifications.Api.csproj Notifications.Api/
RUN dotnet restore Notifications.Api/Notifications.Api.csproj
COPY FCG.Messaging.Contracts/ FCG.Messaging.Contracts/
COPY Notifications.Api/ Notifications.Api/
WORKDIR /src/Notifications.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Notifications.Api.dll"]
