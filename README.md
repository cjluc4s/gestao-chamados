# 🎫 Help Desk Corporativo

Sistema de Gestão de Chamados interno, desenvolvido com foco em fluxo real de atendimento corporativo.

> Projeto de portfólio — arquitetura limpa, regras de negócio reais e controle de acesso por perfis.

---

## 🧱 Stack

- **ASP.NET Core MVC** (.NET 10)
- **Entity Framework Core** (Code First)
- **SQLite** (sem necessidade de SQL Server)
- **ASP.NET Identity** (autenticação + roles)
- **SignalR** (notificações em tempo real)
- **QuestPDF** (exportação de relatórios em PDF)
- **Bootstrap 5** (UI responsiva)
- **API REST** paralela

---

## 👥 Perfis e permissões

| Perfil | Acesso |
|---|---|
| **Usuário** | Cria chamados, comenta e acompanha apenas os próprios |
| **Atendente** | Visualiza chamados sem atribuição, assume, altera status e responde |
| **Admin** | Acesso total: usuários, SLA, dashboard e todos os chamados |

---

## 🔄 Fluxo de status

```
Aberto → Em andamento → Aguardando usuário → Resolvido → Fechado
```

Toda mudança de status é registrada com **quem alterou** e **quando** (auditoria).

---

## ⏱️ SLA por prioridade

| Prioridade | Tempo máximo |
|---|---|
| Crítica | 2 horas |
| Alta | 4 horas |
| Média | 12 horas |
| Baixa | 24 horas |

Chamados com SLA vencido são sinalizados visualmente na listagem e nos detalhes.

---

## 🧩 Funcionalidades

- ✅ Abertura de chamados (título, descrição, prioridade, categoria)
- ✅ Upload de anexos nos chamados (múltiplos arquivos, limite 10 MB)
- ✅ Comentários em thread (diferencia usuário vs suporte)
- ✅ Controle de visibilidade por role
- ✅ Atribuição de chamados (agente assume da fila)
- ✅ Alteração de status com histórico de auditoria
- ✅ Dashboard com indicadores (total, abertos, resolvidos, SLA vencido)
- ✅ Filtros avançados (status, data início/fim)
- ✅ Notificações em tempo real (SignalR)
- ✅ SLA com cálculo automático por prioridade
- ✅ Exportação de dados (CSV e PDF)
- ✅ API REST (`GET /api/tickets`, `GET /api/tickets/{id}`)
- ✅ Seed automático (usuários, roles, categorias)

---

## 🗂️ Entidades

- `ApplicationUser` (Identity com nome completo)
- `Ticket` (chamado principal)
- `TicketComment` (interações/thread)
- `TicketStatusHistory` (auditoria de mudanças)
- `Category` (classificação do chamado)
- `Attachment` (anexos dos chamados)

---

## 🚀 Como executar

### Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Passos

```bash
git clone https://github.com/cjluc4s/gestao-chamados.git
cd gestao-chamados
dotnet run
```

O banco SQLite é criado automaticamente na primeira execução.

### Credenciais de teste

| Perfil | Email | Senha |
|---|---|---|
| Admin | `admin@helpdesk.local` | `P@ssw0rd!` |
| Atendente | `agent@helpdesk.local` | `P@ssw0rd!` |
| Usuário | `user@helpdesk.local` | `P@ssw0rd!` |

> ⚠️ Credenciais apenas para ambiente de desenvolvimento local.

---

## 📊 Estrutura do projeto

```
gestao-chamados/
├── Controllers/
│   ├── HomeController.cs
│   ├── TicketsController.cs
│   ├── DashboardController.cs
│   ├── AdminController.cs
│   └── Api/
│       └── TicketsApiController.cs
├── Data/
│   ├── ApplicationDbContext.cs
│   ├── DbInitializer.cs
│   └── SlaPolicy.cs
├── Hubs/
│   └── NotificationHub.cs
├── Models/
│   ├── ApplicationUser.cs
│   ├── Ticket.cs
│   ├── TicketComment.cs
│   ├── TicketStatusHistory.cs
│   ├── Attachment.cs
│   ├── Category.cs
│   ├── TicketStatus.cs
│   ├── TicketPriority.cs
│   ├── RoleNames.cs
│   └── ViewModels/
├── Views/
│   ├── Home/
│   ├── Tickets/
│   ├── Dashboard/
│   ├── Admin/
│   └── Shared/
└── wwwroot/
    ├── css/
    │   └── site.css
    └── js/
        └── notifications.js
```

---

## 📝 Licença

Projeto de portfólio — uso livre para estudo e referência.
