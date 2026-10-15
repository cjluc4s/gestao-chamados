# 🧠 Sistema de Gestão de Chamados

Help desk web para abrir, atender e acompanhar chamados, com controle de acesso por papel, SLA por prioridade, histórico de status, anexos e notificações em tempo real.

## 🏗️ Stack
- ASP.NET Core MVC + Razor Pages (.NET 10)
- Entity Framework Core 10 (migrations) + SQLite
- ASP.NET Core Identity (autenticação, papéis, bloqueio por tentativas)
- SignalR (notificações em tempo real)
- QuestPDF (relatório em PDF)
- xUnit (testes)

## ▶️ Como rodar

Pré-requisito: [.NET SDK 10](https://dotnet.microsoft.com/download).

```bash
dotnet tool restore
```

```bash
dotnet run
```

O app sobe em `http://localhost:5159` (perfil `https`: `https://localhost:7001`). Na inicialização ele:
1. aplica as migrations pendentes (`Database.MigrateAsync()`), criando o `helpdesk.db` se não existir;
2. cria os papéis Admin, Agent e User;
3. cadastra as categorias padrão (Infraestrutura, Sistemas, Acesso, Hardware);
4. em Development, cria as contas de demonstração abaixo.

### Contas de demonstração

Criadas **somente** com `ASPNETCORE_ENVIRONMENT=Development` (padrão do `launchSettings.json`):

| Papel | E-mail | Senha |
|---|---|---|
| Admin | admin@helpdesk.local | P@ssw0rd! |
| Atendente | agent@helpdesk.local | P@ssw0rd! |
| Usuário | user@helpdesk.local | P@ssw0rd! |

A senha pode ser trocada com `SeedData:DemoPassword` (por exemplo em `appsettings.Development.json`, que fica fora do Git).

Em produção, para criar o primeiro administrador real, defina:

| Variável de ambiente | Obrigatória | Descrição |
|---|---|---|
| `SeedData__AdminEmail` | sim | E-mail/login do Admin |
| `SeedData__AdminPassword` | sim | Senha (mín. 8 caracteres, com maiúscula, número e símbolo) |
| `SeedData__AdminFullName` | não | Nome exibido (padrão: "Administrador") |

Qualquer pessoa pode se cadastrar em `/Identity/Account/Register` e recebe o papel **Usuário**. Um Admin altera papéis em **Usuários** no menu. A mudança vale em até ~1 minuto, sem precisar sair e entrar de novo.

## 🔐 Papéis e permissões

| Ação | Usuário | Atendente | Admin |
|---|---|---|---|
| Ver chamados | os que abriu | os atribuídos a ele e os sem responsável | todos |
| Abrir chamado | ✅ | — (sem botão na lista) | ✅ |
| Comentar e anexar arquivos | nos que pode ver | nos que pode ver | todos |
| Assumir chamado sem responsável | — | ✅ | — |
| Alterar status | — | ✅ | ✅ |
| Reabrir chamado fechado | — | — | ✅ |
| Gerenciar papéis e ver SLAs | — | — | ✅ |

Um atendente **não** vê nem assume chamados que já estão com outro atendente. A mesma regra vale para as telas, o dashboard, as exportações e a API ([`TicketAccessPolicy`](Data/TicketAccessPolicy.cs)).

## 📊 Regras de negócio

### Fluxo de status

```
Aberto → Em andamento ⇄ Aguardando usuário → Resolvido → Fechado
```

Transições permitidas ([`TicketWorkflowPolicy`](Data/TicketWorkflowPolicy.cs)):

| De | Para |
|---|---|
| Aberto | Em andamento, Aguardando usuário, Resolvido, Fechado |
| Em andamento | Aguardando usuário, Resolvido, Fechado |
| Aguardando usuário | Em andamento, Resolvido, Fechado |
| Resolvido | Em andamento (reabertura), Fechado |
| Fechado | — (só o Admin pode reabrir para Em andamento) |

Automatismos:
- Se um atendente muda o status de um chamado sem responsável, ele passa a ser o responsável.
- Se o solicitante responde a um chamado em "Aguardando usuário", o chamado volta para "Em andamento".
- Toda mudança de status fica no **histórico** do chamado, com autor e data.

### SLA

O prazo é calculado na abertura a partir da prioridade ([`SlaPolicy`](Data/SlaPolicy.cs)):

| Prioridade | Prazo |
|---|---|
| Crítica | 2 horas |
| Alta | 4 horas |
| Média | 12 horas |
| Baixa | 24 horas |

O chamado conta como **SLA vencido** se o prazo passou e ele ainda não foi resolvido nem fechado.

### Anexos

- Até **10 MB** por arquivo, várias de uma vez ([`AttachmentPolicy`](Data/AttachmentPolicy.cs)).
- Extensões aceitas: `.jpg .jpeg .png .gif .pdf .doc .docx .xls .xlsx .txt .zip .rar`.
- Arquivos fora da regra são ignorados, e a tela informa quantos foram recusados.
- Os arquivos ficam em `uploads/<id do chamado>/`, com nome aleatório. O banco guarda o caminho relativo, então a pasta pode ser movida junto com o app.

## ⚙️ Funcionalidades

- **Chamados:** abertura com categoria, prioridade e anexos; detalhes com comentários, anexos e histórico.
- **Lista:** filtros por status e período (datas no fuso do servidor) e paginação de 20 em 20.
- **Exportação:** CSV (UTF-8, separador `;`, pronto para o Excel e protegido contra injeção de fórmulas) e PDF, com os filtros aplicados.
- **Dashboard:** total, abertos, resolvidos, SLA vencido e distribuição por prioridade, de acordo com o que cada papel pode ver.
- **Notificações em tempo real (SignalR):**
  - novo chamado → atendentes e admins;
  - mudança de status → solicitante e responsável;
  - novo comentário → solicitante e responsável (se ainda não houver responsável, todos os atendentes);
  - chamado assumido → solicitante.
- **Conta:** login com bloqueio após 5 tentativas (15 min), autocadastro e redefinição de senha.

> **Redefinição de senha:** não há envio de e-mail configurado. O link sai no log da aplicação e, em Development, também aparece na tela de confirmação.

## 🔌 API REST (somente leitura)

Exige login (mesmo cookie do site) e aplica as mesmas regras de visibilidade.

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/tickets?page=1&pageSize=20` | Lista paginada (`pageSize` máx. 100) |
| GET | `/api/tickets/{id}` | Detalhe de um chamado (`403` se não tiver acesso) |

Exemplo de resposta da listagem:

```json
{
  "items": [
    {
      "id": 1,
      "title": "Impressora sem toner",
      "status": 1,
      "priority": 4,
      "createdAtUtc": "2026-10-15T19:26:45.8484497Z",
      "dueAtUtc": "2026-10-15T21:26:45.8484497Z",
      "createdByUserId": "74bd52bf-...",
      "assignedToUserId": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

`status`: 1 Aberto, 2 Em andamento, 3 Aguardando usuário, 4 Resolvido, 5 Fechado.
`priority`: 1 Baixa, 2 Média, 3 Alta, 4 Crítica. Todas as datas estão em UTC.

## 🗂️ Estrutura

```
Areas/Identity/   Páginas de login, cadastro e senha (PT-BR)
Controllers/      MVC (Tickets, Dashboard, Admin) e Api/ (REST)
Data/             DbContext, seed, migrations e políticas de negócio (*Policy.cs)
Hubs/             Hub SignalR de notificações
Models/           Entidades, enums e ViewModels
Views/            Telas Razor
wwwroot/          CSS, JS (cliente SignalR) e bibliotecas front-end
Tests/            Testes xUnit das políticas de negócio
```

As regras de negócio ficam em classes estáticas puras em `Data/` (`TicketAccessPolicy`, `TicketWorkflowPolicy`, `SlaPolicy`, `AttachmentPolicy`, `CsvFormatter`). Os controllers só as aplicam, o que permite testá-las sem banco nem HTTP.

## 🧪 Testes

```bash
dotnet test
```

Cobrem controle de acesso por papel (incluindo a regressão de "roubo" de chamado entre atendentes), transições de status, SLA, anexos e formatação/escape do CSV.

## 🗄️ Migrations

Alterações de schema são versionadas com EF Core Migrations e aplicadas sozinhas no próximo `dotnet run`. Depois de alterar um modelo:

```bash
dotnet ef migrations add NomeDaMigration -o Data/Migrations
```

Para conferir se o modelo está em dia com a última migration:

```bash
dotnet ef migrations has-pending-model-changes
```

## ⚠️ Limitações conhecidas

- **SQLite:** bom para desenvolvimento e uso pequeno. Para produção com vários acessos simultâneos, troque o provider em `Program.cs`.
- **Anexos:** ficam no disco local. Em ambiente com várias instâncias, use armazenamento compartilhado.
- **Fuso horário:** as datas são exibidas e filtradas no fuso do servidor.
