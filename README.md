# 🧠 Sistema de Gestão de Chamados

Help desk web para abrir, atender e acompanhar chamados, com controle de acesso por papel, histórico de status, anexos e notificações em tempo real. Foi pensado para **se antecipar aos problemas** em vez de só registrá-los, e roda inteiro na sua máquina: **sem serviços pagos nem chaves de API**.

## ✨ Diferenciais

| | O que faz |
|---|---|
| ⏱️ **SLA inteligente** | Conta só o horário comercial, **pausa** quando o chamado espera o solicitante, **avisa aos 80%** do prazo e **escalona** ao estourar |
| 📡 **Tempo real nas telas** | Comentários, status e responsável se atualizam sozinhos; mostra quem mais está vendo o chamado e "fulano está digitando…" |
| ⭐ **Satisfação e métricas** | O solicitante avalia o atendimento (1 a 5 estrelas) e uma página de métricas mostra tempos de resposta e resolução, SLA cumprido e CSAT por atendente |
| 🤖 **Regras de automação** | O Admin monta regras "quando… se… então…" (atribuir, subir prioridade, fechar chamados parados, avisar) sem programar |

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
| Editar título, descrição, categoria e prioridade | os que abriu, enquanto Abertos | os que abriu, enquanto Abertos | sempre |
| Cancelar chamado | os que abriu, enquanto Abertos | os que abriu, enquanto Abertos | se não estiver Fechado/Cancelado |
| Assumir chamado sem responsável | — | ✅ | — |
| Atribuir/reatribuir a um atendente | — | — | ✅ |
| Alterar status | — | ✅ | ✅ |
| Reabrir chamado fechado ou cancelado | — | — | ✅ |
| Avaliar o atendimento (CSAT) | os que abriu, depois de resolvidos | — | — |
| Ver métricas | — | só os chamados atribuídos a ele | todos, com ranking por atendente |
| Gerenciar regras de automação | — | — | ✅ |
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
| Cancelado | — (só o Admin pode reabrir para Em andamento) |

**Cancelar é a "exclusão" do sistema:** nada é apagado. O chamado vai para "Cancelado" pelo botão próprio (não aparece em "Alterar status"), sai da contagem de SLA e mantém comentários, anexos e histórico.

Automatismos:
- Se um atendente muda o status de um chamado sem responsável, ele passa a ser o responsável.
- Se o solicitante responde a um chamado em "Aguardando usuário", o chamado volta para "Em andamento".
- Toda mudança de status fica no **histórico** do chamado, com autor e data. Mudanças feitas por regras de automação aparecem como feitas por "Automação".

### SLA inteligente

O prazo base vem da prioridade ([`SlaPolicy`](Data/SlaPolicy.cs)) e é contado a partir da abertura:

| Prioridade | Prazo | Relógio (padrão) |
|---|---|---|
| Crítica | 2 horas | 24x7 |
| Alta | 4 horas | horas úteis |
| Média | 12 horas | horas úteis |
| Baixa | 24 horas | horas úteis |

- **Horário comercial:** por padrão, segunda a sexta, das 08:00 às 18:00 no fuso de São Paulo, com alguns feriados. Um chamado Alto (4 horas úteis) aberto na quinta às 17h vence na sexta às 11h, não de madrugada. Tudo isso se ajusta na seção `Sla` do `appsettings.json` (tabela abaixo) e pode ser conferido na tela **SLA** do menu do Admin.
- **O relógio pausa** enquanto o chamado está em "Aguardando usuário" (a espera do solicitante não conta contra o atendimento) e para de vez em Resolvido, Fechado e Cancelado. Ao retomar ou reabrir, o limite é empurrado pelo tempo que ficou parado ([`SlaCalculator`](Data/SlaCalculator.cs)).
- **Aviso aos 80%:** o responsável (ou todos os atendentes, se não houver responsável) e os admins recebem uma notificação, uma única vez por prazo. O chamado ganha o selo "Em risco".
- **Escalonamento:** ao vencer, o chamado ganha o selo "Escalonado" e os admins e o solicitante são avisados, uma única vez.
- **Troca de prioridade:** o prazo é recalculado **a partir da abertura**: subir para Crítica um chamado aberto há 5 horas o deixa vencido na hora.
- Um chamado já atrasado quando foi pausado ou resolvido continua contado como atrasado, e um resolvido a tempo continua "no prazo" para sempre.

Um serviço em segundo plano ([`SlaMonitorService`](Services/SlaMonitorService.cs)) faz as verificações do aviso e do escalonamento a cada minuto.

| Configuração (`Sla:`) | Padrão | Descrição |
|---|---|---|
| `WarnAtPercent` | `80` | Percentual do prazo em que o chamado entra "em risco" |
| `MonitorIntervalSeconds` | `60` | Intervalo da verificação de alertas |
| `AlwaysOnPriorities` | `["Critical"]` | Prioridades cujo relógio corre 24x7 |
| `BusinessHours:Enabled` | `true` | `false` faz todo o SLA correr 24 horas por dia |
| `BusinessHours:TimeZone` | `America/Sao_Paulo` | Fuso do expediente (id IANA ou do Windows; se não existir, usa o fuso do servidor) |
| `BusinessHours:Start` / `End` | `08:00` / `18:00` | Expediente |
| `BusinessHours:Days` | segunda a sexta | Dias úteis |
| `BusinessHours:Holidays` | alguns feriados nacionais | Datas `yyyy-MM-dd` sem expediente |

Uma configuração inválida (por exemplo, fim do expediente antes do início) impede o app de subir, com a mensagem do problema.

### Anexos

- Até **10 MB** por arquivo, várias de uma vez ([`AttachmentPolicy`](Data/AttachmentPolicy.cs)).
- Extensões aceitas: `.jpg .jpeg .png .gif .pdf .doc .docx .xls .xlsx .txt .zip .rar`.
- Arquivos fora da regra são ignorados, e a tela informa quantos foram recusados.
- Os arquivos ficam em `uploads/<id do chamado>/`, com nome aleatório. O banco guarda o caminho relativo, então a pasta pode ser movida junto com o app.

## ⚙️ Funcionalidades

- **Chamados:** abertura com categoria, prioridade e anexos; detalhes com comentários, anexos e histórico; edição, cancelamento e atribuição pelo Admin (ver permissões acima).
- **Lista:** filtros por status e período (datas no fuso do servidor) e paginação de 20 em 20.
- **Exportação:** CSV (UTF-8, separador `;`, pronto para o Excel e protegido contra injeção de fórmulas) e PDF, com os filtros aplicados.
- **Dashboard:** total, em aberto, resolvidos, **em risco**, SLA vencido, distribuição por prioridade, percentuais de resolução e de prazo e satisfação média, de acordo com o que cada papel pode ver. Chamados cancelados (exclusão lógica) não entram nos indicadores, mas continuam na lista, filtrando por "Cancelado".
- **Notificações em tempo real (SignalR):**
  - novo chamado → atendentes e admins;
  - mudança de status → solicitante e responsável;
  - novo comentário → solicitante e responsável (se ainda não houver responsável, todos os atendentes);
  - chamado assumido → solicitante;
  - chamado atribuído pelo Admin → novo responsável, solicitante e responsável anterior (devolvido à fila → todos os atendentes);
  - chamado editado → solicitante e responsável (sem responsável, todos os atendentes);
  - chamado cancelado → solicitante e responsável;
  - SLA em risco → responsável (ou atendentes) e admins; SLA vencido → também o solicitante;
  - chamado resolvido → o solicitante é convidado a avaliar; avaliação recebida → responsável e admins;
  - mudanças feitas por regras de automação → quem foi afetado.
- **Conta:** login com bloqueio após 5 tentativas (15 min), autocadastro e redefinição de senha.

> **Redefinição de senha:** não há envio de e-mail configurado. O link sai no log da aplicação e, em Development, também aparece na tela de confirmação.

## 📡 Tempo real nas telas

Quem está com um chamado aberto vê as mudanças **sem recarregar** ([`live.js`](wwwroot/js/live.js)):

- **Detalhes:** comentários, status, responsável, anexos, SLA e histórico se atualizam sozinhos, com um breve destaque no trecho que mudou. Se você está digitando ou escolhendo algo naquele trecho, ele espera você terminar.
- **Presença:** "Fulano também está vendo" e "Fulano está digitando…", só para quem tem acesso ao chamado (a regra é a mesma das telas e da API).
- **Lista de chamados:** aparece o aviso "Há atualizações na lista" (a lista só muda quando você clica, para não mexer no que está lendo).
- **Dashboard:** os números se atualizam sozinhos.
- Um indicador "Ao vivo / Reconectando… / Sem conexão" mostra o estado, e depois de uma queda a tela busca o que perdeu.

Por trás, o servidor avisa só quem pode ver cada chamado ([`TicketNotifier`](Services/TicketNotifier.cs)) e a página busca a si mesma e troca os trechos, então o que aparece é sempre o que o servidor renderizou para aquela pessoa.

## ⭐ Satisfação (CSAT) e métricas

**Avaliação:** quando o chamado é resolvido, o solicitante recebe um aviso e vê um cartão para dar de 1 a 5 estrelas e, se quiser, um comentário. É uma avaliação por chamado (garantida também por índice único no banco) e aparece para a equipe na hora.

**Métricas** (menu **Métricas**, para Admin e Atendente; período de 7, 30 ou 90 dias):

| Indicador | Como é calculado |
|---|---|
| Abertos / Resolvidos | Criados no período / com data de resolução no período |
| Em fila agora | Chamados que ainda pedem atendimento |
| Tempo até a 1ª resposta | Do chamado aberto até o primeiro comentário de alguém da equipe (que não seja o próprio solicitante) |
| Tempo de resolução | Da abertura até a resolução, **sem contar a espera pelo solicitante**, no mesmo relógio do SLA |
| SLA cumprido | % dos resolvidos cujo prazo não estourou antes de o relógio parar |
| Satisfação (CSAT) | Média das notas dos chamados resolvidos no período, com a taxa de avaliação |

Há também o gráfico de chamados por dia (SVG puro, sem biblioteca), a distribuição por prioridade, categoria e nota e, para o Admin, o ranking por atendente. O Atendente vê apenas os chamados atribuídos a ele. Chamados cancelados não entram. O cálculo é uma função pura ([`MetricsCalculator`](Data/MetricsCalculator.cs)).

## 🤖 Regras de automação

Em **Automação** (só Admin) cada regra é uma frase: **quando** algo acontece, **se** o chamado combina, **então** a ação é feita.

| Quando (gatilho) | Se (condições, todas opcionais) | Então (ação) |
|---|---|---|
| Chamado criado | Categoria | Atribuir a um atendente |
| Status alterado | Prioridade | Mudar a prioridade (recalcula o SLA) |
| SLA em risco | Status | Mudar o status (segue o fluxo) |
| SLA vencido | Texto cita palavras (ignora maiúsculas e acentos) | Avisar os admins |
| Inatividade (parado há N horas) | Horas parado (só na inatividade) | Avisar o responsável (ou a fila) ou o solicitante |

Exemplos prontos na própria tela: fechar chamados resolvidos há 3 dias sem retorno, lembrar o solicitante após 2 dias de espera, cancelar após 7 dias sem resposta, avisar os admins quando um chamado crítico vencer e subir a prioridade por palavra-chave.

Como funciona por dentro ([`AutomationEngine`](Services/AutomationEngine.cs)):
- as regras de um mesmo evento rodam em ordem de criação, cada uma vendo o chamado já alterado pelas anteriores;
- o que a automação muda **não** dispara novas regras (sem laços);
- regras de inatividade agem **uma vez por período parado**: só voltam a agir depois que alguém mexer no chamado;
- mudanças de status ficam no histórico como feitas por "Automação" (um usuário interno, sem login e oculto da lista de usuários);
- cada execução fica registrada e as últimas aparecem na tela; pausar uma regra não apaga nada, excluir apaga o histórico dela.

## 🎨 Interface

A interface é responsiva (celular, tablet e desktop), tem tema claro e escuro e não depende de nenhum serviço externo além do cliente SignalR. O design system está em [`wwwroot/css/site.css`](wwwroot/css/site.css), com tokens de cor no topo do arquivo.

### Cores e significado

Cada cor tem um único significado em todo o sistema, e nenhum estado depende só da cor (sempre há texto ou ícone):

| Cor | Significado | Onde aparece |
|---|---|---|
| Azul | Confiança e estabilidade | Marca, botões principais, status "Aberto" |
| Ciano | Fluxo e atividade | "Em andamento" |
| Violeta | Pausa e espera | "Aguardando usuário" |
| Verde | Alívio e conclusão | "Resolvido", SLA no prazo |
| Âmbar / laranja | Atenção, sem alarme | Pendências, prioridades Média e Alta |
| Vermelho | Urgência | **Só** SLA vencido e prioridade Crítica |
| Cinza | Neutro, encerrado | "Fechado", "Cancelado", prioridade Baixa |

Reservar o vermelho para o que é urgente faz com que ele seja notado quando aparece.

### Interações
- **Tema:** segue o sistema e pode ser trocado pelo botão no menu; a escolha fica salva no navegador.
- **Notificações:** além do aviso na tela, o sino do menu guarda as últimas 15 da sessão, com contador de novas.
- **Lista de chamados:** chips de status, linhas clicáveis e, no celular, cartões no lugar da tabela.
- **Formulários:** contador de caracteres, mostrar/ocultar senha, anexos por arrastar e soltar (com aviso de arquivo inválido antes de enviar), botão com indicador de carregamento e proteção contra envio duplicado.
- **Ações destrutivas** (como cancelar um chamado) pedem confirmação em um modal.
- **Usuários (Admin):** busca instantânea.
- **Acessibilidade:** link para pular ao conteúdo, foco visível, rótulos em ícones e respeito à preferência "reduzir movimento" do sistema.

O JavaScript é opcional: sem ele as páginas continuam funcionando, apenas sem essas interações ([`ui.js`](wwwroot/js/ui.js) e [`notifications.js`](wwwroot/js/notifications.js)).

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

`status`: 1 Aberto, 2 Em andamento, 3 Aguardando usuário, 4 Resolvido, 5 Fechado, 6 Cancelado.
`priority`: 1 Baixa, 2 Média, 3 Alta, 4 Crítica. Todas as datas estão em UTC.

## 🗂️ Estrutura

```
Areas/Identity/   Páginas de login, cadastro e senha (PT-BR)
Controllers/      MVC (Tickets, Dashboard, Metrics, Automation, Admin) e Api/ (REST)
Data/             DbContext, seed, migrations e regras de negócio (calendário e cálculo de SLA, políticas, métricas, automação)
Hubs/             Hub SignalR (notificações, presença e "digitando")
Models/           Entidades, enums e ViewModels
Services/         Notificador, monitor de SLA, motor de automação e presença
Views/            Telas Razor (Shared/ tem o layout, os ícones SVG e o menu do usuário)
wwwroot/          css/site.css (design system) e js/: ui.js (interações), notifications.js (SignalR), live.js (tempo real)
Tests/            Testes xUnit (regras, banco SQLite em memória e migrations)
```

As regras de negócio ficam em classes puras em `Data/` (`TicketAccessPolicy`, `TicketWorkflowPolicy`, `TicketAssignmentPolicy`, `TicketLifecycle`, `FeedbackPolicy`, `SlaPolicy`, `SlaCalculator`, `BusinessCalendar`, `MetricsCalculator`, `RuleMatcher`, `AttachmentPolicy`, `CsvFormatter`). Os controllers só as aplicam, o que permite testá-las sem HTTP; o que mexe no banco é testado contra um SQLite em memória com as migrations reais.

## 🧪 Testes

```bash
dotnet test
```

Mais de 220 testes cobrem: controle de acesso por papel (incluindo a regressão de "roubo" de chamado entre atendentes), transições de status, quem pode editar/cancelar, atribuição pelo Admin, o calendário de horário comercial e o cálculo do SLA (pausa, retomada, risco, vencimento), os alertas e o escalonamento, a presença em tempo real e o público de cada aviso, as métricas e a avaliação, o motor e as condições das regras de automação, anexos, o CSV e as **migrations** (aplicadas do zero e com preenchimento dos dados antigos).

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
- **Fuso horário:** as datas são exibidas e filtradas no fuso do servidor; o expediente do SLA usa o fuso configurado em `Sla:BusinessHours:TimeZone`.
- **Uma instância:** a presença em tempo real e o monitor de SLA/automação rodam em memória no próprio processo. Para várias instâncias seria preciso um backplane do SignalR e eleger quem roda o monitor.
