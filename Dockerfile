FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
COPY . /source
WORKDIR /source

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish -c Release -r linux-x64 --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .

RUN adduser -D nocteschat
USER nocteschat

EXPOSE 3000
ENTRYPOINT ["dotnet", "NoctesChat.dll"]
