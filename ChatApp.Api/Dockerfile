FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ChatApp.Api/ChatApp.Api.csproj ChatApp.Api/
COPY ChatApp.Infrastructure/ChatApp.Infrastructure.csproj ChatApp.Infrastructure/
RUN dotnet restore ChatApp.Api/ChatApp.Api.csproj

COPY . .
RUN dotnet publish ChatApp.Api/ChatApp.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "ChatApp.Api.dll"]
