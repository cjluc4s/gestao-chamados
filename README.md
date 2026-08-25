# 🧠 Sistema de Gestão de Chamados

Sistema web completo para gerenciamento de chamados com controle de acesso, SLA, auditoria e atualização em tempo real.

## 🏗️ Arquitetura
- ASP.NET Core MVC (.NET 10)
- Entity Framework Core (migrations) + SQLite
- SignalR (tempo real)
- Identity (autenticação, autorização e papéis)
- QuestPDF (exportação de relatórios)

## 🔐 Perfis de usuário
- Usuário: abertura e acompanhamento de chamados
- Atendente: tratamento e atualização
- Administrador: controle total do sistema, incluindo atribuição de papéis

## ⚙️ Funcionalidades
- CRUD completo de chamados
- Controle de status (workflow completo)
- Definição de SLA por prioridade
- Auditoria de ações
- Atualizações em tempo real (SignalR)
- Exportação em CSV e PDF
- Dashboard com métricas
- API REST somente leitura (`/api/tickets`)

## 📊 Regras de negócio
- Fluxo: Aberto → Em andamento → Aguardando usuário → Resolvido → Fechado
- Controle por perfil de acesso
- SLA baseado em prioridade

## ▶️ Como rodar

```bash
dotnet tool restore
dotnet run
```

`dotnet run` aplica as migrations automaticamente no início (`Database.MigrateAsync()`), cria o banco SQLite local e popula categorias padrão.

### Contas de acesso

Em ambiente **Development**, contas de demonstração são criadas automaticamente:

| Papel | Email | Senha |
|---|---|---|
| Admin | admin@helpdesk.local | P@ssw0rd! |
| Atendente | agent@helpdesk.local | P@ssw0rd! |
| Usuário | user@helpdesk.local | P@ssw0rd! |

Essas contas **não** são criadas fora de Development. Para produção, defina as variáveis de ambiente `SeedData__AdminEmail` e `SeedData__AdminPassword` (opcionalmente `SeedData__AdminFullName`) para provisionar o primeiro administrador real.

Novos usuários também podem se autocadastrar (`/Identity/Account/Register`) — recebem o papel "Usuário" por padrão. Um Admin pode alterar o papel de qualquer usuário em **Usuários** no menu.

## 🧪 Testes

```bash
dotnet test
```

Cobre as regras de negócio críticas: política de SLA, política de anexos e controle de acesso a chamados por papel (`Data/*Policy.cs`, testados em `Tests/`).

## 🗄️ Migrations

Alterações de schema são versionadas via EF Core Migrations. Para criar uma nova migration após alterar um modelo:

```bash
dotnet ef migrations add NomeDaMigration -o Data/Migrations
```
