# 🧠 Sistema de Gestão de Chamados

Sistema web completo para gerenciamento de chamados com controle de acesso, SLA, auditoria e atualização em tempo real.

## 🚀 Demonstração
(Coloque prints ou vídeo aqui)

## 🏗️ Arquitetura
- ASP.NET Core MVC
- Entity Framework Core
- SQLite
- SignalR (tempo real)
- Identity (autenticação e autorização)

## 🔐 Perfis de usuário
- Usuário: abertura e acompanhamento de chamados
- Atendente: tratamento e atualização
- Administrador: controle total do sistema

## ⚙️ Funcionalidades
- CRUD completo de chamados
- Controle de status (workflow completo)
- Definição de SLA por prioridade
- Auditoria de ações
- Atualizações em tempo real (SignalR)
- Exportação em PDF
- Dashboard com métricas

## 📊 Regras de negócio
- Fluxo: Aberto → Em andamento → Aguardando usuário → Resolvido → Fechado
- Controle por perfil de acesso
- SLA baseado em prioridade

## 🧠 Diferenciais técnicos
- Implementação de tempo real com SignalR
- Separação de responsabilidades (Controllers / Services)
- Integração com API REST
- Geração de relatórios em PDF

## ▶️ Como rodar
```bash
dotnet run
