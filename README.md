# ChatZ

WhatsApp, Telegram i Viber u jednom prozoru (WPF, .NET 10).

- **WhatsApp** i **Telegram** rade kroz WebView2 (web.whatsapp.com, web.telegram.org), svaki sa svojim sačuvanim profilom.
- **Viber**: ChatZ pokreće instalirani Viber Desktop i ugrađuje njegov prozor u treći tab.
- Brojač nepročitanih na dugmićima, u naslovu, na taskbaru i u tray tooltipu (WhatsApp, Telegram).
- X sklanja program u tray; „Izlaz“ iz menija ga gasi (Viber tada ostaje u svom trayu).
- Pokretanje sa Windowsom (počinje sakriven u trayu).
- Prečice: `Ctrl+1/2/3` menja servis, `F5` osvežava aktivni.

## Šta treba na računaru

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Microsoft Edge WebView2 Runtime (na Windows 10/11 je obično već tu)
- Viber za Windows (za Viber tab)

## Build

```
dotnet build -c Release
```

ili otvori `ChatZ.csproj` u Visual Studiju. Na GitHubu svaki push na `main` pravi `ChatZ-win-x64.zip`
i release `v<Version>` (verzija iz `ChatZ.csproj`). Za novu verziju podigni `<Version>`.

Podaci (prijave, podešavanja): `%LOCALAPPDATA%\ChatZ`.

## Napomena o Viberu

Ugrađivanje tuđeg prozora obično radi dobro, ali posle nekog Viber update-a može da zatreba popravka.
Ako Viber nije pronađen, u Viber tabu klikni „Izaberi Viber.exe…“. Pri prvom pokretanju Viber treba jednom
aktivirati (QR kod sa telefona) — to radiš direktno u Viber tabu.
