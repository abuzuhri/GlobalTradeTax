FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/GlobalTradeTax.Api/*.csproj src/GlobalTradeTax.Api/
RUN dotnet restore src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj
COPY src/GlobalTradeTax.Api/ src/GlobalTradeTax.Api/
RUN dotnet publish src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN mkdir -p /app/saleor-state && chown $APP_UID /app/saleor-state
USER $APP_UID
ENTRYPOINT ["dotnet", "GlobalTradeTax.Api.dll"]
