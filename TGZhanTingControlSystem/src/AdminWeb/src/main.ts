import './style.css'
import { api, ApiError, resolveAssetUrl, type AssetKind, type ClientRuntimeStatus, type ContentDraftSnapshot, type ContentVersionSummary, type ExhibitionModule, type NarrationAudioCandidateEvaluation, type NarrationAudioDraftStatus, type NarrationNode, type NarrationRoute, type OperationalEvent, type PlaybackSessionStatus, type PublishedContent, type SystemReadiness, type TtsProviderDescriptor, type TtsSynthesisConfiguration, type UiExperienceConfig } from './api'
import { bindingStatusLabel, jobStatusLabel, TtsWorkflowController, type TtsWorkflowApi } from './tts-workflow'

const app = document.querySelector<HTMLDivElement>('#app')
if (!app) throw new Error('App root was not found')
const root = app
let content: PublishedContent | null = null
let ttsProviders: TtsProviderDescriptor[] = []
let draftBaseVersion = 0
let draftRevision = 0
let draftStatuses: NarrationAudioDraftStatus[] = []
let draftConflict = ''
let draftMutationSequence = 0
let draftSyncedSequence = 0
let draftSyncTimer: number | null = null
let draftSyncInFlight: Promise<void> | null = null
let ttsController: TtsWorkflowController | null = null
let username = ''
let dirty = false
let editingModuleId: string | null = null
let editingNodeId: string | null = null
let clientStatuses: ClientRuntimeStatus[] = []
let playbackSessions: PlaybackSessionStatus[] = []
let uiConfig: UiExperienceConfig | null = null
let routes: NarrationRoute[] = []
let readiness: SystemReadiness | null = null
let versions: ContentVersionSummary[] = []
let operationEvents: OperationalEvent[] = []
let publishError = ''
let activeView: 'content'|'routes'|'versions'|'operations' = 'content'
const draftKey = 'tg-content-draft-v1'

const ttsWorkflowApi: TtsWorkflowApi = {
  listProviders: api.ttsProviders,
  createJob: api.createTtsJob,
  getJob: api.getTtsJob,
  cancelJob: api.cancelTtsJob,
  getCandidate: api.getTtsCandidate,
  evaluateCandidate: api.evaluateTtsCandidate,
  adoptCandidate: api.adoptTtsCandidate,
}

const escapeHtml = (value: string | null | undefined): string => (value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character] ?? character)
const assetKindName = (kind: AssetKind): string => ['宣传视频', '展示图片', '动画素材', '讲解音频'][kind] ?? '素材'
const formatSize = (bytes: number): string => bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(2)} GB` : bytes >= 1024 ** 2 ? `${(bytes / 1024 ** 2).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

function renderLogin(message = ''): void {
  root.innerHTML = `<main class="login-shell"><section class="login-brand"><p class="eyebrow">TG EXHIBITION CONTROL</p><h1>展厅自动讲解<br/>管理平台</h1><p>统一管理 12 个展陈模块、宣传视频、讲解文案与内容发布。</p><div class="brand-stats"><span>双屏联动</span><span>TTS 讲解</span><span>组合路线</span></div></section><section class="login-panel"><form id="login-form" class="login-card"><div class="login-mark">TG</div><h2>欢迎登录</h2><p class="login-tip">请输入具有内容发布权限的管理账号</p><label>用户名<input id="username" autocomplete="username" value="admin" /></label><label>密码<input id="password" type="password" autocomplete="current-password" placeholder="请输入密码" /></label><p id="login-error" class="login-error">${escapeHtml(message)}</p><button class="primary login-button" type="submit">登录管理平台</button><p class="login-foot">TG 展厅中控系统 · 内部管理入口</p></form></section></main>`
  root.querySelector<HTMLFormElement>('#login-form')?.addEventListener('submit', login)
  root.querySelector<HTMLInputElement>('#password')?.focus()
}

async function login(event: SubmitEvent): Promise<void> {
  event.preventDefault()
  const form = event.currentTarget as HTMLFormElement
  const button = form.querySelector<HTMLButtonElement>('button')!
  const error = form.querySelector<HTMLParagraphElement>('#login-error')!
  button.disabled = true; button.textContent = '正在登录…'; error.textContent = ''
  try { const result = await api.login(form.querySelector<HTMLInputElement>('#username')!.value.trim(), form.querySelector<HTMLInputElement>('#password')!.value); username = result.username; await loadDashboard() }
  catch (reason) { error.textContent = reason instanceof Error ? reason.message : '登录失败'; button.disabled = false; button.textContent = '登录管理平台' }
}

async function loadDashboard(): Promise<void> {
  root.innerHTML = '<main class="loading">正在读取正式内容版本…</main>'
  try {
    draftConflict = ''
    const [published, serverDraft, providers, clients, sessions, ui, routeResult, ready, history, events] = await Promise.all([
      api.getContent(), api.getDraft(), api.ttsProviders().catch(() => []), api.clientStatuses(), api.playbackSessions(), api.getUi(), api.routes(), api.readiness(), api.contentVersions(), api.operations()
    ])
    const restored = restoreDraft(published, serverDraft)
    content = restored.content; ttsProviders = providers; clientStatuses = clients; playbackSessions = sessions; uiConfig = ui
    draftBaseVersion = serverDraft.baseContentVersion; draftRevision = serverDraft.revision; draftStatuses = serverDraft.narrationAudioStatuses
    routes = routeResult.routes ?? []; readiness = ready; versions = history; operationEvents = events; dirty = restored.dirty
    draftMutationSequence = restored.needsSync ? 1 : 0; draftSyncedSequence = 0; renderDashboard()
    if (restored.needsSync) void syncDraftNow()
  } catch (error) { root.innerHTML = `<main class="error"><h1>无法连接管理服务</h1><p>${escapeHtml(error instanceof Error ? error.message : String(error))}</p></main>` }
}

function renderDashboard(): void {
  if (!content) return
  const availableProviders = ttsProviders.filter(item => item.available && item.voices.length > 0)
  root.innerHTML = `<header class="topbar"><div><p class="eyebrow">TG EXHIBITION CONTROL</p><h1>展厅自动讲解管理平台</h1><p class="meta">正式版本 V${content.version} · 草稿 r${draftRevision} · ${content.modules.length} 个模块 · 发布人 ${escapeHtml(content.publishedBy || '系统初始化')}</p></div><div class="actions"><div class="tts-badge ${availableProviders.length ? 'ready' : ''}"><span>语音合成服务</span><strong>${availableProviders.length ? `${availableProviders.length} 个可用 Provider` : '当前未配置'}</strong></div><div class="account"><span>当前账号</span><strong>${escapeHtml(username)}</strong></div><button id="logout">退出</button><button id="publish" class="primary" ${dirty && !draftConflict ? '' : 'disabled'}>发布新版本</button></div></header>
    <nav class="workspace-nav">${navButton('content','内容中心')}${navButton('routes','讲解路线')}${navButton('versions','发布与回滚')}${navButton('operations','终端与运行')}</nav>
    <main>${renderWorkspace()}</main><div id="toast" class="toast" aria-live="polite"></div>`
  root.querySelector('#publish')?.addEventListener('click', publish)
  root.querySelector('#logout')?.addEventListener('click', async () => { disposeNodeRuntime(); await api.logout(); content = null; username = ''; renderLogin() })
  root.querySelectorAll<HTMLButtonElement>('[data-view]').forEach(button => button.addEventListener('click',()=>{activeView=button.dataset.view as typeof activeView;renderDashboard()}))
  if(activeView==='content'){
    root.querySelector('#add')?.addEventListener('click', addModule)
    root.querySelector('#edit-ui')?.addEventListener('click', openUiEditor)
    root.querySelectorAll<HTMLElement>('[data-module]').forEach(bindCard)
  }
  if(activeView==='routes') bindRouteManagement()
  if(activeView==='versions') bindVersionManagement()
  if(activeView==='operations') root.querySelector('#refresh-operations')?.addEventListener('click', refreshOperations)
}

function navButton(view:typeof activeView,label:string):string{return `<button data-view="${view}" class="${activeView===view?'active':''}">${label}</button>`}

function renderWorkspace():string{
  if(!content)return ''
  if(activeView==='routes')return renderRoutesView()
  if(activeView==='versions')return renderVersionsView()
  if(activeView==='operations')return renderOperationsView()
  return `${draftConflict?`<section class="publish-validation-error"><strong>草稿同步冲突</strong><p>${escapeHtml(draftConflict)}</p></section>`:''}${publishError?`<section class="publish-validation-error"><strong>发布已阻止：请修复以下素材</strong><p>${escapeHtml(publishError).replaceAll('；','<br/>')}</p></section>`:''}<section class="summary"><article><strong>${content.modules.filter(item => item.enabled).length}</strong><span>已启用模块</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.length, 0)}</strong><span>讲解节点</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.flatMap(node => node.assets).length + (item.nodes.filter(node => node.ttsAudioUrl).length), 0)}</strong><span>展示与音频素材</span></article><article><strong>${dirty ? '待发布' : '已同步'}</strong><span>编辑状态</span></article><article><strong>${clientStatuses.filter(client => client.online).length}/${Math.max(2, clientStatuses.length)}</strong><span>终端在线</span></article><article><strong>${playbackSessions.length}</strong><span>进行中讲解</span></article></section>
  <section class="section-title"><div><p class="eyebrow">CONTENT</p><h2>内容中心</h2><p class="section-note">配置模块、讲解节点、讲解语音和大屏素材；修改会安全保存到服务器草稿，发布后终端才会更新。</p></div><div class="section-actions"><button id="edit-ui">终端界面设置</button><button id="add">新增模块</button></div></section><section class="module-grid">${content.modules.sort((a,b) => a.order-b.order).map(moduleCard).join('')}</section>`
}

function renderRoutesView():string{
  if(!content)return ''
  const cards=routes.length?routes.map(route=>{
    const modules=route.moduleIds.map(id=>content!.modules.find(item=>item.id===id)).filter((item):item is ExhibitionModule=>Boolean(item))
    const seconds=modules.reduce((total,module)=>total+module.nodes.reduce((sum,node)=>sum+Math.max(0,...node.assets.map(asset=>asset.durationSeconds||0)),0),0)
    return `<article class="management-card"><div class="card-kicker">${modules.length} 个主题${seconds>0?` · 约 ${Math.ceil(seconds/60)} 分钟`:''}</div><h3>${escapeHtml(route.name)}</h3><p>${escapeHtml(modules.map(item=>item.name).join(' → ')||'路线中的主题已失效')}</p><footer><span>更新于 ${formatTime(route.updatedAtUtc)}</span><button data-edit-route="${route.id}">编辑</button><button data-delete-route="${route.id}" class="danger-text">删除</button></footer></article>`
  }).join(''):'<div class="empty-management"><strong>尚未创建常用路线</strong><p>创建后，中控接待首页可以直接一键开始讲解。</p></div>'
  return `${readinessBanner()}<section class="section-title"><div><p class="eyebrow">ROUTES</p><h2>讲解路线</h2><p class="section-note">统一维护正式接待路线；中控端仍可建立临时组合。</p></div><div class="section-actions"><button id="add-route" class="primary">新增路线</button></div></section><section class="management-grid">${cards}</section>`
}

function bindRouteManagement():void{
  root.querySelector('#add-route')?.addEventListener('click',()=>openRouteEditor(null))
  root.querySelectorAll<HTMLElement>('[data-edit-route]').forEach(button=>button.addEventListener('click',()=>openRouteEditor(routes.find(item=>item.id===button.dataset.editRoute)??null)))
  root.querySelectorAll<HTMLElement>('[data-delete-route]').forEach(button=>button.addEventListener('click',async()=>{
    const route=routes.find(item=>item.id===button.dataset.deleteRoute);if(!route||!window.confirm(`确定删除路线“${route.name}”吗？`))return
    try{await api.deleteRoute(route.id);routes=(await api.routes()).routes;renderDashboard();showToast('路线已删除。')}catch(error){showToast(error instanceof Error?error.message:'删除路线失败')}
  }))
}

function openRouteEditor(route:NarrationRoute|null):void{
  if(!content)return
  const modal=document.createElement('div');modal.className='modal-backdrop';modal.id='route-modal'
  modal.innerHTML=`<section class="route-editor-modal"><header class="editor-header"><div><p class="eyebrow">ROUTE EDITOR</p><h2>${route?'编辑讲解路线':'新建讲解路线'}</h2></div><button id="close-route" class="icon-button">×</button></header><div class="route-editor-body"><label>路线名称<input id="route-name" maxlength="30" value="${escapeHtml(route?.name??'')}" placeholder="例如：重要客户接待路线"/></label><strong>按讲解顺序选择主题</strong><p class="section-note">点击主题加入路线；使用箭头调整顺序。</p><div id="route-order" class="route-order"></div><div class="route-module-picker">${content.modules.filter(item=>item.enabled).sort((a,b)=>a.order-b.order).map(item=>`<button data-pick-module="${item.id}"><span>${String(item.order).padStart(2,'0')}</span>${escapeHtml(item.name)}</button>`).join('')}</div><footer class="editor-footer"><span>保存后中控端会自动刷新常用路线。</span><button id="cancel-route">取消</button><button id="save-route" class="primary">保存路线</button></footer></div></section>`
  document.body.appendChild(modal)
  const order=[...(route?.moduleIds??[])].filter(id=>content!.modules.some(item=>item.id===id&&item.enabled))
  const renderOrder=()=>{
    const holder=modal.querySelector<HTMLElement>('#route-order')!;holder.innerHTML=order.length?order.map((id,index)=>{const item=content!.modules.find(module=>module.id===id)!;return `<article><span>${index+1}</span><strong>${escapeHtml(item.name)}</strong><button data-up="${id}" ${index===0?'disabled':''}>↑</button><button data-down="${id}" ${index===order.length-1?'disabled':''}>↓</button><button data-remove="${id}">移除</button></article>`}).join(''):'<div class="empty-order">尚未选择主题</div>'
    modal.querySelectorAll<HTMLElement>('[data-up]').forEach(button=>button.addEventListener('click',()=>move(button.dataset.up!,-1)))
    modal.querySelectorAll<HTMLElement>('[data-down]').forEach(button=>button.addEventListener('click',()=>move(button.dataset.down!,1)))
    modal.querySelectorAll<HTMLElement>('[data-remove]').forEach(button=>button.addEventListener('click',()=>{const index=order.indexOf(button.dataset.remove!);if(index>=0)order.splice(index,1);renderOrder()}))
    modal.querySelectorAll<HTMLButtonElement>('[data-pick-module]').forEach(button=>button.classList.toggle('selected',order.includes(button.dataset.pickModule!)))
  }
  const move=(id:string,direction:number)=>{const index=order.indexOf(id),target=index+direction;if(index<0||target<0||target>=order.length)return;order.splice(index,1);order.splice(target,0,id);renderOrder()}
  modal.querySelectorAll<HTMLElement>('[data-pick-module]').forEach(button=>button.addEventListener('click',()=>{const id=button.dataset.pickModule!;if(!order.includes(id))order.push(id);renderOrder()}))
  const close=()=>modal.remove();modal.querySelector('#close-route')?.addEventListener('click',close);modal.querySelector('#cancel-route')?.addEventListener('click',close)
  modal.querySelector('#save-route')?.addEventListener('click',async()=>{const name=modal.querySelector<HTMLInputElement>('#route-name')!.value.trim();if(!name)return showToast('请输入路线名称。');if(!order.length)return showToast('请至少选择一个主题。');try{await api.saveRoute({id:route?.id??null,name,moduleIds:order});routes=(await api.routes()).routes;close();renderDashboard();showToast('路线已保存。')}catch(error){showToast(error instanceof Error?error.message:'保存路线失败')}})
  renderOrder();modal.querySelector<HTMLInputElement>('#route-name')?.focus()
}

function renderVersionsView():string{
  const rows=versions.map(item=>`<article class="version-row ${item.current?'current':''}"><div><strong>V${item.version}${item.current?' · 当前版本':''}</strong><p>${item.moduleCount} 个模块 · ${item.nodeCount} 个节点 · ${escapeHtml(item.publishedBy)}</p></div><time>${formatTime(item.publishedAtUtc)}</time><button data-rollback="${item.version}" ${item.current?'disabled':''}>回滚到此版本</button></article>`).join('')
  return `<section class="section-title"><div><p class="eyebrow">RELEASES</p><h2>发布与回滚</h2><p class="section-note">回滚不会覆盖历史，而是使用旧内容生成一个新的正式版本。</p></div></section><section class="version-list">${rows||'<div class="empty-management">暂无发布历史</div>'}</section>`
}

function bindVersionManagement():void{
  root.querySelectorAll<HTMLElement>('[data-rollback]').forEach(button=>button.addEventListener('click',async()=>{const version=Number(button.dataset.rollback);if(!window.confirm(`确定将正式内容回滚到 V${version} 吗？`))return;try{content=await api.rollbackContent(version);const draft=await api.getDraft();draftBaseVersion=draft.baseContentVersion;draftRevision=draft.revision;draftStatuses=draft.narrationAudioStatuses;draftConflict='';dirty=false;draftMutationSequence=0;draftSyncedSequence=0;localStorage.removeItem(draftKey);versions=await api.contentVersions();renderDashboard();showToast(`已回滚并生成新版本 V${content.version}。`)}catch(error){showToast(error instanceof Error?error.message:'回滚失败')}}))
}

function renderOperationsView():string{
  const clients=clientStatuses.map(client=>`<article class="terminal-card ${client.online&&client.ready?'ready':''}"><div><strong>${client.kind===1?'LED播放端':'触控中控端'}</strong><span>${client.online?'在线':'离线'}</span></div><p>${escapeHtml(client.clientId)} · 应用 ${escapeHtml(client.appVersion||'-')}</p><p>内容版本 V${client.contentVersion??0} · ${escapeHtml(client.status??(client.ready?'已就绪':'未就绪'))}</p><small>最后心跳 ${formatTime(client.lastSeenUtc)}</small></article>`).join('')
  const sessions=playbackSessions.length?playbackSessions.map(item=>`<article class="session-row"><strong>${escapeHtml(item.moduleName)} / ${escapeHtml(item.nodeName)}</strong><span>${item.paused?'已暂停':item.playPublished?'正在讲解':`准备中 ${Math.round((item.preparationProgress||0)*100)}%`}</span><small>${item.currentNodeNumber}/${item.totalNodes} · 内容 V${item.contentVersion}</small></article>`).join(''):'<div class="empty-inline">当前没有进行中的讲解</div>'
  const logs=operationEvents.map(item=>`<tr><td>${formatTime(item.occurredAtUtc)}</td><td><span class="log-level ${item.level.toLowerCase()}">${escapeHtml(item.level)}</span></td><td>${escapeHtml(item.category)} / ${escapeHtml(item.action)}</td><td>${escapeHtml(item.message)}</td></tr>`).join('')
  return `${readinessBanner()}<section class="section-title"><div><p class="eyebrow">OPERATIONS</p><h2>终端与运行</h2><p class="section-note">查看终端在线、内容版本、活动讲解和关键操作记录。</p></div><button id="refresh-operations">刷新状态</button></section><section class="terminal-grid">${clients||'<div class="empty-management">尚无终端注册</div>'}</section><h3 class="subheading">活动讲解</h3><section class="session-list">${sessions}</section><h3 class="subheading">运行日志</h3><div class="log-table-wrap"><table class="log-table"><thead><tr><th>时间</th><th>级别</th><th>分类</th><th>事件</th></tr></thead><tbody>${logs||'<tr><td colspan="4">暂无运行日志</td></tr>'}</tbody></table></div>`
}

function readinessBanner():string{const state=!readiness?.canStart?'blocked':readiness.ledReady?'ready':'degraded';const label=state==='ready'?'✓ 系统可以接待':state==='degraded'?'! 系统受限可用':'! 系统暂未就绪';return `<section class="readiness-banner ${state}"><div><span>${label}</span><strong>${escapeHtml(readiness?.message??'正在读取系统状态')}</strong></div><p>服务器内容 V${readiness?.contentVersion??0} · LED内容 V${readiness?.ledContentVersion??0}</p></section>`}

async function refreshOperations():Promise<void>{try{[clientStatuses,playbackSessions,readiness,operationEvents]=await Promise.all([api.clientStatuses(),api.playbackSessions(),api.readiness(),api.operations()]);renderDashboard();showToast('运行状态已刷新。')}catch(error){showToast(error instanceof Error?error.message:'刷新失败')}}
function formatTime(value:string):string{const date=new Date(value);return Number.isNaN(date.getTime())?'-':date.getUTCFullYear()<2000?'系统初始化':date.toLocaleString('zh-CN',{hour12:false})}

function moduleCard(module: ExhibitionModule): string { return `<article class="module-card ${module.enabled ? '' : 'disabled'}" data-module="${module.id}"><div class="module-number">${String(module.order).padStart(2,'0')}</div><div class="module-body"><div class="module-heading"><input class="module-name" value="${escapeHtml(module.name)}" aria-label="模块名称"/><label class="switch"><input type="checkbox" class="module-enabled" ${module.enabled?'checked':''}/><span></span></label></div><textarea class="module-description" placeholder="填写模块简介">${escapeHtml(module.description)}</textarea><div class="module-footer"><span>${module.nodes.length} 个讲解节点 · ${module.nodes.reduce((sum,node)=>sum+node.assets.length+(node.ttsAudioUrl?1:0),0)} 个素材</span><button class="node-button">编辑内容</button></div></div></article>` }

function bindCard(card: HTMLElement): void {
  const module = content?.modules.find(item => item.id === card.dataset.module); if (!module) return
  card.querySelector<HTMLInputElement>('.module-name')?.addEventListener('input', event => { module.name=(event.target as HTMLInputElement).value; markDirty() })
  card.querySelector<HTMLTextAreaElement>('.module-description')?.addEventListener('input', event => { module.description=(event.target as HTMLTextAreaElement).value; markDirty() })
  card.querySelector<HTMLInputElement>('.module-enabled')?.addEventListener('change', event => { module.enabled=(event.target as HTMLInputElement).checked; markDirty(true) })
  card.querySelector('.node-button')?.addEventListener('click', () => openNodeEditor(module.id))
}

function openNodeEditor(moduleId: string): void {
  const module = content?.modules.find(item => item.id === moduleId); if (!module) return
  editingModuleId = moduleId
  if (!module.nodes.some(node => node.id === editingNodeId)) editingNodeId = module.nodes.sort((a,b)=>a.order-b.order)[0]?.id ?? null
  renderNodeEditor()
}

function renderNodeEditor(): void {
  disposeNodeRuntime()
  document.querySelector('#node-modal')?.remove()
  const module = content?.modules.find(item => item.id === editingModuleId); if (!module) return
  const nodes = module.nodes.sort((a,b)=>a.order-b.order)
  const node = nodes.find(item => item.id === editingNodeId) ?? null
  const modal = document.createElement('div'); modal.id = 'node-modal'; modal.className = 'modal-backdrop'
  modal.innerHTML = `<section class="editor-modal"><header class="editor-header"><div><p class="eyebrow">CONTENT EDITOR</p><h2>${escapeHtml(module.name)} · 讲解内容</h2></div><button id="close-editor" class="icon-button">×</button></header><div class="editor-layout"><aside class="node-list"><div class="node-list-title"><strong>讲解节点</strong><button id="add-node">＋ 新增</button></div>${nodes.length ? nodes.map(item => `<button class="node-list-item ${item.id===editingNodeId?'active':''}" data-node-id="${item.id}"><span>${String(item.order).padStart(2,'0')}</span><strong>${escapeHtml(item.name || '未命名节点')}</strong><small>${item.assets.length} 个大屏素材</small></button>`).join('') : '<div class="empty-node">暂无节点<br/>点击“新增”创建第一段讲解</div>'}</aside><div class="node-editor">${node ? nodeForm(node) : '<div class="empty-editor"><strong>尚未创建讲解节点</strong><p>每个节点可同时配置讲解文案、TTS音频和LED宣传视频。</p></div>'}</div></div></section>`
  document.body.appendChild(modal)
  modal.querySelector('#close-editor')?.addEventListener('click', closeNodeEditor)
  modal.addEventListener('click', event => { if (event.target === modal) closeNodeEditor() })
  modal.querySelector('#add-node')?.addEventListener('click', () => addNode(module))
  modal.querySelectorAll<HTMLElement>('[data-node-id]').forEach(button => button.addEventListener('click', () => { editingNodeId=button.dataset.nodeId!; renderNodeEditor() }))
  if (node) { initializeTtsController(module, node, modal); bindNodeForm(module, node, modal) }
}

function nodeForm(node: NarrationNode): string {
  return `<div class="editor-fields"><div class="field-row"><label>节点名称<input id="node-name" value="${escapeHtml(node.name)}"/></label><label class="short-field">顺序<input id="node-order" type="number" min="1" value="${node.order}"/></label><label class="short-field">故障策略<select id="failure-policy"><option value="0" ${node.failurePolicy===0?'selected':''}>跳过继续</option><option value="1" ${node.failurePolicy===1?'selected':''}>停止讲解</option></select></label></div><div class="field-row"><label>声画混音策略<select id="audio-mix-policy"><option value="0" ${node.audioMixPolicy===0?'selected':''}>讲解时压低视频原声</option><option value="1" ${node.audioMixPolicy===1?'selected':''}>保留视频原声音量</option><option value="2" ${node.audioMixPolicy===2?'selected':''}>讲解时静音视频</option></select></label><label class="short-field">讲解时视频音量<input id="video-volume" type="number" min="0" max="1" step="0.05" value="${node.videoVolume || 0.25}"/></label><label class="short-field">讲解音量<input id="narration-volume" type="number" min="0" max="1" step="0.05" value="${node.narrationVolume || 1}"/></label></div><label>讲解文案<textarea id="narration-text" class="narration-text" placeholder="输入供TTS合成和讲解员查看的完整文案">${escapeHtml(node.narrationText)}</textarea></label>
    <section id="tts-workspace" class="tts-workspace">${ttsPanel(node)}</section>
    <section class="assets-section"><div class="assets-heading"><div><strong>LED大屏素材</strong><p>建议1920×1080 H.264 MP4；超大视频将直接流式写入服务器磁盘。</p></div></div><div class="upload-row"><select id="asset-kind"><option value="0">宣传视频</option><option value="1">展示图片</option><option value="2">动画素材</option></select><label>时长（秒）<input id="asset-duration" type="number" min="0" step="0.1" value="0"/></label><label class="upload-button">选择并上传<input id="asset-file" type="file" accept="video/*,image/*,.mov,.mkv,.webm"/></label><div id="upload-progress" class="upload-progress"><span></span></div></div><div class="asset-list">${node.assets.length ? node.assets.map(asset => `<article><div class="asset-icon">${asset.kind===0?'▶':asset.kind===1?'▧':'◆'}</div><div><strong>${escapeHtml(asset.name)}</strong><p>${assetKindName(asset.kind)} · ${formatSize(asset.sizeBytes)}${asset.durationSeconds?` · ${asset.durationSeconds}s`:''}</p></div><a href="${escapeHtml(asset.url)}" target="_blank">查看</a><button data-remove-asset="${asset.id}">移除</button></article>`).join('') : '<div class="empty-assets">尚未上传大屏素材</div>'}</div></section>
    <footer class="editor-footer"><span>修改保存在当前草稿中，点击主页面“发布新版本”后终端才会更新。</span><button id="delete-node" class="danger">删除当前节点</button><button id="done-node" class="primary">完成编辑</button></footer></div>`
}

function ttsPanel(node: NarrationNode): string {
  const status = draftStatuses.find(item => item.moduleId === editingModuleId && item.nodeId === node.id)
  const statusValue = status?.status ?? (node.narrationAudio ? 5 : node.ttsAudioUrl ? 6 : 0)
  const availableProviders = ttsProviders.filter(item => item.available && item.voices.length > 0)
  const configuration = selectTtsConfiguration(node, availableProviders)
  const selectedProvider = configuration
    ? availableProviders.find(item => item.providerId === configuration.providerKey) ?? null
    : null
  const selectedVoice = selectedProvider?.voices.find(item => item.voiceId === configuration?.voice) ?? null
  const workflow = ttsController?.current ?? null
  const candidate = workflow?.candidate ?? null
  const evaluation = workflow?.evaluation ?? null
  const adopted = node.narrationAudio
  const currentAudioUrl = adopted?.asset.url ?? node.ttsAudioUrl
  const currentOrigin = adopted ? ['TTS 生成', '人工上传', '旧版语音'][adopted.origin] : node.ttsAudioUrl ? '旧版语音' : '未配置'
  const providerOptions = availableProviders.map(provider =>
    `<option value="${escapeHtml(provider.providerId)}" ${configuration?.providerKey === provider.providerId ? 'selected' : ''}>${escapeHtml(provider.displayName)}${provider.developmentOnly ? '（开发测试）' : ''}</option>`).join('')
  const voiceOptions = (selectedProvider?.voices ?? []).map(voice =>
    `<option value="${escapeHtml(voice.voiceId)}" ${configuration?.voice === voice.voiceId ? 'selected' : ''}>${escapeHtml(selectedProvider?.developmentOnly ? '开发测试音色' : voice.displayName)} · ${escapeHtml(voice.language)}</option>`).join('')
  const providersUnavailable = !availableProviders.length
    ? `<div class="tts-empty"><strong>当前未配置语音合成服务</strong><p>生成操作已禁用；仍可继续使用下方“上传讲解音频”。</p>${ttsProviders.filter(item => !item.available).map(item => `<small>${escapeHtml(item.displayName)}：${escapeHtml(item.unavailableReason ?? '当前不可用')}</small>`).join('')}</div>`
    : ''
  const developmentWarning = selectedProvider?.developmentOnly
    ? '<div class="tts-development-note">当前选择的是开发测试音色，仅用于流程验证，不代表正式商用语音效果。</div>'
    : ''
  const currentAudio = currentAudioUrl
    ? `<article class="tts-current-audio"><div><span class="tts-card-label">${draftRevision > 0 ? '当前草稿已采用语音 · 等待发布' : '当前已发布语音'}</span><strong>${escapeHtml(adopted?.asset.name ?? currentAudioUrl.split('/').pop() ?? '讲解音频')}</strong><p>${escapeHtml(currentOrigin)}${adopted ? ` · ${formatSize(adopted.asset.sizeBytes)} · ${escapeHtml(adopted.synthesisConfiguration.voice || '人工音频')}` : ''}</p></div><a href="${escapeHtml(resolveAssetUrl(currentAudioUrl))}" target="_blank" rel="noreferrer">打开音频</a><button id="remove-audio" class="danger-text">移除</button></article>`
    : '<div class="tts-current-empty">当前尚未采用讲解语音</div>'
  const job = workflow?.job
  const jobView = job
    ? `<div class="tts-job ${job.status === 3 ? 'error' : job.status === 2 ? 'success' : ''}"><span>${jobStatusLabel(job.status)}</span><small>任务创建于 ${formatTime(job.createdAtUtc)}${job.retryCount ? ` · 已重试 ${job.retryCount} 次` : ''}</small>${job.status === 0 || job.status === 1 ? '<button id="cancel-tts">取消生成</button>' : ''}</div>`
    : ''
  const draftPending = draftSyncedSequence < draftMutationSequence
  const candidateView = candidate
    ? `<article class="tts-candidate"><header><div><span class="tts-card-label">待采用的候选语音</span><strong>${escapeHtml(ttsProviders.find(item => item.providerId === candidate.providerId)?.developmentOnly ? '开发测试音色' : candidate.voice)}</strong></div><span class="status-pill ${evaluation?.adoptable ? 'fresh' : 'warning'}">${evaluation?.adoptable ? '可以采用' : '需要重新确认'}</span></header><dl><div><dt>Provider</dt><dd>${escapeHtml(ttsProviders.find(item => item.providerId === candidate.providerId)?.displayName ?? candidate.providerId)}</dd></div><div><dt>生成时间</dt><dd>${formatTime(candidate.createdAtUtc)}</dd></div><div><dt>时长</dt><dd>${candidate.validation.durationSeconds > 0 ? `${candidate.validation.durationSeconds.toFixed(1)} 秒` : '未提供'}</dd></div><div><dt>匹配检查</dt><dd>${evaluation ? `${evaluation.narrationTextMatches ? '文案一致' : '文案已变化'} · ${evaluation.synthesisConfigurationMatches ? '配置一致' : '配置已变化'}` : '正在由服务器检查'}</dd></div></dl>${evaluation && !evaluation.adoptable ? `<p class="candidate-warning">${escapeHtml(candidateEvaluationMessage(evaluation))}</p>` : ''}<footer><button id="preview-candidate">${workflow?.previewing ? '暂停试听' : '播放试听'}</button><button id="replay-candidate">重新播放</button><button id="abandon-candidate">放弃候选</button><button id="adopt-candidate" class="primary" ${evaluation?.adoptable && !workflow?.adopting && !draftConflict && !draftPending ? '' : 'disabled'}>${workflow?.adopting ? '正在采用…' : '采用到草稿'}</button></footer></article>`
    : ''
  const workflowError = workflow?.error ? `<div class="tts-error">${escapeHtml(workflow.error)}</div>` : ''
  const unsynced = draftPending
    ? '<div class="tts-sync-note">草稿变更正在提交服务器，状态检查完成后才能采用候选语音。</div>'
    : ''
  return `<div class="tts-heading"><div><strong>讲解语音</strong><p>生成、试听和采用彼此独立；采用后仍需发布才会进入正式内容。</p></div><span class="status-pill status-${statusValue}">${bindingStatusLabel(statusValue)}</span></div>
    ${statusValue !== 1 ? `<p class="tts-status-message">${bindingStatusDescription(statusValue)}</p>` : ''}
    ${currentAudio}
    <div class="tts-config-grid"><label>Provider<select id="tts-provider" ${providersUnavailable ? 'disabled' : ''}>${providerOptions}</select></label><label>Voice<select id="tts-voice" ${providersUnavailable ? 'disabled' : ''}>${voiceOptions}</select></label><label>Language<input id="tts-language" value="${escapeHtml(configuration?.language ?? selectedVoice?.language ?? 'zh-CN')}" readonly/></label><label>Rate<input id="tts-rate" type="number" step="0.05" min="${selectedProvider?.capabilities.minRate ?? 0.5}" max="${selectedProvider?.capabilities.maxRate ?? 2}" value="${configuration?.rate ?? 1}" ${providersUnavailable ? 'disabled' : ''}/></label><label>Pitch<input id="tts-pitch" type="number" step="0.05" min="${selectedProvider?.capabilities.minPitch ?? -1}" max="${selectedProvider?.capabilities.maxPitch ?? 1}" value="${configuration?.pitch ?? 0}" ${providersUnavailable ? 'disabled' : ''}/></label><button id="generate-tts" class="primary" ${providersUnavailable || !node.narrationText.trim() || workflow?.generating || Boolean(draftConflict) ? 'disabled' : ''}>${workflow?.generating ? '正在生成…' : '生成候选语音'}</button></div>
    ${providersUnavailable}${developmentWarning}${unsynced}${jobView}${workflowError}${candidateView}
    <div class="tts-manual-upload"><div><strong>人工上传讲解音频</strong><p>上传文件同样进入不可变素材与完整性校验体系。</p></div><label class="upload-audio">选择音频<input id="audio-file" type="file" accept="audio/*,.mp3,.wav,.m4a,.aac"/></label></div>`
}

function bindingStatusDescription(status: number): string {
  return [
    '当前节点尚未采用讲解音频。',
    '',
    '服务器检测到讲解词与已采用语音不一致，重新生成并采用后才能恢复有效状态。',
    '服务器检测到音色或合成参数已变化，重新生成并采用后才能恢复有效状态。',
    '已绑定音频缺少有效素材、大小或校验信息，请重新生成或上传。',
    '语音绑定信息不完整，请重新生成或上传。',
    '该语音来自旧版本，只能兼容展示，需重新生成或上传后才能获得完整校验。',
  ][status] ?? '服务器暂时无法确认当前语音状态。'
}

function candidateEvaluationMessage(evaluation: NarrationAudioCandidateEvaluation): string {
  if (!evaluation.candidateExists) return '候选语音不存在或已经失效，请重新生成。'
  if (!evaluation.locationMatches) return '候选语音对应的模块或节点已经变化，请重新生成。'
  if (!evaluation.narrationTextMatches) return '讲解词已修改，旧候选语音不能采用，请重新生成。'
  if (!evaluation.synthesisConfigurationMatches) return '音色或合成参数已变化，旧候选语音不能采用，请重新生成。'
  if (!evaluation.assetValid) return '候选音频未通过完整性验证，请重新生成。'
  return '候选语音当前不可采用。'
}

function selectTtsConfiguration(node: NarrationNode, providers = ttsProviders.filter(item => item.available && item.voices.length > 0)): TtsSynthesisConfiguration | null {
  if (!providers.length) return null
  const existingProvider = providers.find(item => item.providerId === node.ttsConfiguration?.providerKey)
  const provider = existingProvider ?? providers[0]
  const existingVoice = provider.voices.find(item => item.voiceId === node.ttsConfiguration?.voice)
  const voice = existingVoice ?? provider.voices[0]
  if (!voice) return null
  return {
    providerKey: provider.providerId,
    voice: voice.voiceId,
    language: voice.language,
    rate: existingProvider && existingVoice ? node.ttsConfiguration?.rate ?? 1 : 1,
    pitch: existingProvider && existingVoice ? node.ttsConfiguration?.pitch ?? 0 : 0,
    volume: existingProvider && existingVoice ? node.ttsConfiguration?.volume ?? 1 : 1,
    outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav',
    sampleRateHz: existingProvider && existingVoice ? node.ttsConfiguration?.sampleRateHz ?? 24000 : 24000,
    channels: existingProvider && existingVoice ? node.ttsConfiguration?.channels ?? 1 : 1,
  }
}

function configurationFromEditor(node: NarrationNode, modal: HTMLElement): TtsSynthesisConfiguration | null {
  const providerId = modal.querySelector<HTMLSelectElement>('#tts-provider')?.value
  const provider = ttsProviders.find(item => item.available && item.providerId === providerId)
  const voiceId = modal.querySelector<HTMLSelectElement>('#tts-voice')?.value
  const voice = provider?.voices.find(item => item.voiceId === voiceId)
  if (!provider || !voice) return null
  const rate = Number(modal.querySelector<HTMLInputElement>('#tts-rate')?.value ?? 1)
  const pitch = Number(modal.querySelector<HTMLInputElement>('#tts-pitch')?.value ?? 0)
  return {
    providerKey: provider.providerId,
    voice: voice.voiceId,
    language: voice.language,
    rate: Math.min(provider.capabilities.maxRate, Math.max(provider.capabilities.minRate, rate)),
    pitch: Math.min(provider.capabilities.maxPitch, Math.max(provider.capabilities.minPitch, pitch)),
    volume: node.ttsConfiguration?.volume ?? 1,
    outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav',
    sampleRateHz: node.ttsConfiguration?.sampleRateHz ?? 24000,
    channels: node.ttsConfiguration?.channels ?? 1,
  }
}

function initializeTtsController(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  ttsController = new TtsWorkflowController(ttsWorkflowApi, module.id, node.id, state => {
    if (state.providersLoaded) ttsProviders = [...state.providers]
    refreshTtsWorkspace(module, node, modal)
  })
  void ttsController.loadProviders()
}

function refreshTtsWorkspace(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  if (!document.body.contains(modal) || editingModuleId !== module.id || editingNodeId !== node.id) return
  const workspace = modal.querySelector<HTMLElement>('#tts-workspace')
  if (!workspace) return
  workspace.innerHTML = ttsPanel(node)
  bindTtsActions(module, node, modal)
}

function disposeNodeRuntime(): void {
  ttsController?.dispose()
  ttsController = null
}

function bindTtsActions(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  modal.querySelector('#remove-audio')?.addEventListener('click', () => {
    node.ttsAudioUrl = null; node.narrationAudio = null; node.ttsConfiguration = null
    markDirty(); renderNodeEditor()
  })
  modal.querySelector<HTMLInputElement>('#audio-file')?.addEventListener('change', event =>
    uploadSelectedAsset(node, event.target as HTMLInputElement, true))
  const providerSelect = modal.querySelector<HTMLSelectElement>('#tts-provider')
  providerSelect?.addEventListener('change', () => {
    const provider = ttsProviders.find(item => item.providerId === providerSelect.value)
    const voice = provider?.voices[0]
    if (!provider || !voice) return
    node.ttsConfiguration = { providerKey: provider.providerId, voice: voice.voiceId, language: voice.language, rate: 1, pitch: 0, volume: 1, outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav', sampleRateHz: 24000, channels: 1 }
    markDirty(); refreshTtsWorkspace(module, node, modal)
  })
  modal.querySelector<HTMLSelectElement>('#tts-voice')?.addEventListener('change', () => {
    const configuration = configurationFromEditor(node, modal)
    if (!configuration) return
    node.ttsConfiguration = configuration; markDirty(); refreshTtsWorkspace(module, node, modal)
  })
  ;['#tts-rate', '#tts-pitch'].forEach(selector => modal.querySelector<HTMLInputElement>(selector)?.addEventListener('change', () => {
    const configuration = configurationFromEditor(node, modal)
    if (!configuration) return
    node.ttsConfiguration = configuration; markDirty(); refreshTtsWorkspace(module, node, modal)
  }))
  modal.querySelector('#generate-tts')?.addEventListener('click', async () => {
    const configuration = configurationFromEditor(node, modal)
    if (!node.narrationText.trim()) return showToast('请先填写讲解文案。')
    if (!configuration) return showToast('当前未配置可用的语音合成服务。')
    node.ttsConfiguration = configuration; markDirty()
    try {
      await syncDraftNow()
      if (draftConflict) return
      const retryFailed = ttsController?.current.job?.status === 3 || ttsController?.current.job?.status === 4
      await ttsController?.generate(node.narrationText, configuration, retryFailed)
    } catch { /* syncDraftNow already shows a safe error */ }
  })
  modal.querySelector('#cancel-tts')?.addEventListener('click', () => void ttsController?.cancel())
  const candidateUrl = ttsController?.current.candidate?.asset.url
  modal.querySelector('#preview-candidate')?.addEventListener('click', () => { if (candidateUrl) void ttsController?.togglePreview(resolveAssetUrl(candidateUrl)) })
  modal.querySelector('#replay-candidate')?.addEventListener('click', () => { if (candidateUrl) void ttsController?.replay(resolveAssetUrl(candidateUrl)) })
  modal.querySelector('#abandon-candidate')?.addEventListener('click', () => ttsController?.abandon())
  modal.querySelector('#adopt-candidate')?.addEventListener('click', async () => {
    try {
      await syncDraftNow()
      await ttsController?.refreshEvaluation()
      if (!ttsController?.current.evaluation?.adoptable) return showToast('候选语音已过期或草稿已变化，请重新生成。')
      const result = await ttsController.adopt(draftBaseVersion, draftRevision)
      if (!result) {
        const message = ttsController.current.error
        if (message.includes('内容已发生变化') || message.includes('版本已发生变化')) {
          draftConflict = '当前内容已发生变化，请刷新后重新确认。'
          root.querySelector<HTMLButtonElement>('#publish')?.setAttribute('disabled', '')
        }
        if (message) showToast(message)
        return
      }
      applyDraftSnapshot(result.draft)
      dirty = true; saveLocalDraft(); showToast('候选语音已采用到草稿，发布后才会进入正式内容。')
      renderNodeEditor()
    } catch (error) { handleDraftError(error) }
  })
}

function bindNodeForm(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  const bind = <T extends HTMLInputElement|HTMLTextAreaElement|HTMLSelectElement>(selector:string, update:(element:T)=>void) => modal.querySelector<T>(selector)?.addEventListener('input', event => { update(event.target as T); markDirty() })
  bind<HTMLInputElement>('#node-name', input => node.name=input.value)
  bind<HTMLInputElement>('#node-order', input => node.order=Math.max(1,Number(input.value)||1))
  bind<HTMLSelectElement>('#failure-policy', select => node.failurePolicy=Number(select.value) as 0|1)
  bind<HTMLSelectElement>('#audio-mix-policy', select => node.audioMixPolicy=Number(select.value) as 0|1|2)
  bind<HTMLInputElement>('#video-volume', input => node.videoVolume=Math.min(1,Math.max(0,Number(input.value)||0)))
  bind<HTMLInputElement>('#narration-volume', input => node.narrationVolume=Math.min(1,Math.max(0,Number(input.value)||0)))
  modal.querySelector<HTMLTextAreaElement>('#narration-text')?.addEventListener('input', event => {
    node.narrationText=(event.target as HTMLTextAreaElement).value;markDirty();refreshTtsWorkspace(module,node,modal)
  })
  modal.querySelector('#done-node')?.addEventListener('click', closeNodeEditor)
  modal.querySelector('#delete-node')?.addEventListener('click', () => { module.nodes=module.nodes.filter(item=>item.id!==node.id); editingNodeId=module.nodes[0]?.id??null; markDirty(); renderNodeEditor() })
  modal.querySelectorAll<HTMLElement>('[data-remove-asset]').forEach(button => button.addEventListener('click', () => { node.assets=node.assets.filter(asset=>asset.id!==button.dataset.removeAsset); markDirty(); renderNodeEditor() }))
  modal.querySelector<HTMLInputElement>('#asset-file')?.addEventListener('change', event => uploadSelectedAsset(node, event.target as HTMLInputElement, false))
  bindTtsActions(module,node,modal)
}

async function uploadSelectedAsset(node: NarrationNode, input: HTMLInputElement, narrationAudio: boolean): Promise<void> {
  const file=input.files?.[0]; if(!file)return
  const modal=document.querySelector<HTMLElement>('#node-modal')!
  const kind=narrationAudio?3:Number(modal.querySelector<HTMLSelectElement>('#asset-kind')?.value??0) as AssetKind
  const duration=narrationAudio?0:Number(modal.querySelector<HTMLInputElement>('#asset-duration')?.value??0)
  const progress=modal.querySelector<HTMLElement>('#upload-progress')!; progress.classList.add('visible')
  try {
    const asset=await api.uploadAsset(file,kind,duration,percent=>{const bar=progress.querySelector<HTMLElement>('span');if(bar)bar.style.width=`${percent}%`})
    if(narrationAudio) {
      const binding=await api.bindManualNarrationAudio(asset,node.narrationText)
      node.narrationAudio=binding;node.ttsConfiguration=binding.synthesisConfiguration;node.ttsAudioUrl=binding.asset.url
    } else node.assets.push(asset)
    markDirty()
    await syncDraftNow()
    renderNodeEditor(); showToast(`${file.name} 上传成功。`)
  } catch(error){progress.classList.remove('visible');showToast(error instanceof Error?error.message:'上传失败')}
}

function addNode(module: ExhibitionModule): void { const order=Math.max(0,...module.nodes.map(item=>item.order))+1; const node:NarrationNode={id:crypto.randomUUID(),name:`讲解节点 ${order}`,order,narrationText:'',ttsAudioUrl:null,assets:[],failurePolicy:0,audioMixPolicy:0,videoVolume:0.25,narrationVolume:1,ttsConfiguration:null,narrationAudio:null}; module.nodes.push(node);editingNodeId=node.id;markDirty();renderNodeEditor() }
function closeNodeEditor(): void { disposeNodeRuntime(); document.querySelector('#node-modal')?.remove(); editingModuleId=null;editingNodeId=null;renderDashboard() }

function openUiEditor(): void {
  if (!uiConfig) return
  const config = uiConfig
  const modal = document.createElement('div'); modal.id='ui-modal'; modal.className='modal-backdrop'
  modal.innerHTML=`<section class="ui-editor-modal"><header class="editor-header"><div><p class="eyebrow">TERMINAL APPEARANCE</p><h2>终端界面设置</h2></div><button id="close-ui" class="icon-button">×</button></header><div class="ui-editor-body">
    <section class="ui-config-card"><div class="ui-config-title"><div><strong>触控中控端</strong><p>标题、配色和背景图发布后由中控端自动获取。</p></div><span>UGUI · 1920×1080</span></div><div class="ui-fields"><label>主标题<input id="touch-title" value="${escapeHtml(config.touchTitle)}"/></label><label>副标题<input id="touch-subtitle" value="${escapeHtml(config.touchSubtitle)}"/></label><div class="field-row colors"><label>背景颜色<input id="touch-bg-color" type="color" value="${escapeHtml(config.touchBackgroundColor)}"/></label><label>强调颜色<input id="touch-accent" type="color" value="${escapeHtml(config.touchAccentColor)}"/></label></div><div class="media-setting"><div><strong>背景图</strong><p>${config.touchBackgroundUrl?escapeHtml(config.touchBackgroundUrl.split('/').pop()):'未设置，使用纯色背景'}</p></div><label class="upload-button">上传替换<input id="touch-background-file" type="file" accept="image/*"/></label><button id="clear-touch-background">移除</button></div></div></section>
    <section class="ui-config-card"><div class="ui-config-title"><div><strong>LED 大屏待机界面</strong><p>支持纯色、图片或循环视频；每段讲解结束后自动返回。</p></div><span>UGUI · 1920×1080</span></div><div class="ui-fields"><label>主标题<input id="led-title" value="${escapeHtml(config.ledTitle)}"/></label><label>提示文字<input id="led-subtitle" value="${escapeHtml(config.ledSubtitle)}"/></label><div class="field-row colors"><label>背景颜色<input id="led-bg-color" type="color" value="${escapeHtml(config.ledBackgroundColor)}"/></label><div class="visibility-options"><label class="check-field"><input id="led-show-branding" type="checkbox" ${config.ledShowBranding?'checked':''}/>叠加标题文字</label><label class="check-field"><input id="led-show-status" type="checkbox" ${config.ledShowStatus?'checked':''}/>显示在线状态</label></div></div><div class="media-setting"><div><strong>待机素材</strong><p>${config.ledIdleMediaUrl?`${config.ledIdleMediaKind==='video'?'循环视频':'背景图片'} · ${escapeHtml(config.ledIdleMediaUrl.split('/').pop())}`:'未设置，使用纯色待机页'}</p></div><label class="upload-button">上传图片<input id="led-image-file" type="file" accept="image/*"/></label><label class="upload-button">上传视频<input id="led-video-file" type="file" accept="video/*,.mov,.mkv,.webm"/></label><button id="clear-led-media">移除</button></div></div></section>
    <div id="ui-upload-progress" class="upload-progress"><span></span></div><footer class="editor-footer"><span>界面配置独立发布，不会修改讲解内容版本。</span><button id="cancel-ui">取消</button><button id="save-ui" class="primary">发布界面配置</button></footer></div></section>`
  document.body.appendChild(modal)
  const read=()=>{config.touchTitle=modal.querySelector<HTMLInputElement>('#touch-title')!.value;config.touchSubtitle=modal.querySelector<HTMLInputElement>('#touch-subtitle')!.value;config.touchBackgroundColor=modal.querySelector<HTMLInputElement>('#touch-bg-color')!.value;config.touchAccentColor=modal.querySelector<HTMLInputElement>('#touch-accent')!.value;config.ledTitle=modal.querySelector<HTMLInputElement>('#led-title')!.value;config.ledSubtitle=modal.querySelector<HTMLInputElement>('#led-subtitle')!.value;config.ledBackgroundColor=modal.querySelector<HTMLInputElement>('#led-bg-color')!.value;config.ledShowBranding=modal.querySelector<HTMLInputElement>('#led-show-branding')!.checked;config.ledShowStatus=modal.querySelector<HTMLInputElement>('#led-show-status')!.checked}
  const close=()=>modal.remove()
  modal.querySelector('#close-ui')?.addEventListener('click',close);modal.querySelector('#cancel-ui')?.addEventListener('click',close)
  modal.querySelector('#clear-touch-background')?.addEventListener('click',()=>{config.touchBackgroundUrl=null;openUiEditor();modal.remove()})
  modal.querySelector('#clear-led-media')?.addEventListener('click',()=>{config.ledIdleMediaUrl=null;config.ledIdleMediaKind='none';openUiEditor();modal.remove()})
  modal.querySelector<HTMLInputElement>('#touch-background-file')?.addEventListener('change',event=>uploadUiAsset(event.target as HTMLInputElement,1,'touch',modal))
  modal.querySelector<HTMLInputElement>('#led-image-file')?.addEventListener('change',event=>uploadUiAsset(event.target as HTMLInputElement,1,'led-image',modal))
  modal.querySelector<HTMLInputElement>('#led-video-file')?.addEventListener('change',event=>uploadUiAsset(event.target as HTMLInputElement,0,'led-video',modal))
  modal.querySelector('#save-ui')?.addEventListener('click',async()=>{read();const button=modal.querySelector<HTMLButtonElement>('#save-ui')!;button.disabled=true;button.textContent='正在发布…';try{uiConfig=await api.publishUi(config);close();showToast(`界面配置 V${uiConfig.version} 发布成功，终端将在 10 秒内更新。`)}catch(error){button.disabled=false;button.textContent='发布界面配置';showToast(error instanceof Error?error.message:'界面配置发布失败')}})
}

async function uploadUiAsset(input:HTMLInputElement,kind:AssetKind,target:'touch'|'led-image'|'led-video',modal:HTMLElement):Promise<void>{
  const file=input.files?.[0];if(!file||!uiConfig)return
  const progress=modal.querySelector<HTMLElement>('#ui-upload-progress')!;progress.classList.add('visible')
  try{const asset=await api.uploadAsset(file,kind,0,percent=>{const bar=progress.querySelector<HTMLElement>('span');if(bar)bar.style.width=`${percent}%`});if(target==='touch')uiConfig.touchBackgroundUrl=asset.url;else{uiConfig.ledIdleMediaUrl=asset.url;uiConfig.ledIdleMediaKind=target==='led-video'?'video':'image'};modal.remove();openUiEditor();showToast(`${file.name} 上传成功，请点击“发布界面配置”。`)}catch(error){progress.classList.remove('visible');showToast(error instanceof Error?error.message:'界面素材上传失败')}
}
function markDirty(rerender=false): void {
  dirty = true
  draftMutationSequence++
  saveLocalDraft()
  scheduleDraftSync()
  if (rerender) renderDashboard()
  else if (!draftConflict) root.querySelector<HTMLButtonElement>('#publish')?.removeAttribute('disabled')
}
function addModule(): void { if(!content)return;const order=Math.max(0,...content.modules.map(item=>item.order))+1;content.modules.push({id:crypto.randomUUID(),name:'新模块',order,description:'',coverUrl:null,enabled:true,nodes:[]});markDirty(true) }

function validateDraft(modules: ExhibitionModule[]): string | null {
  for(const module of modules){if(!module.name.trim())return `第 ${module.order} 个模块名称不能为空。`;for(const node of module.nodes){if(!node.name.trim())return `${module.name}存在未命名节点。`;if(!node.narrationText.trim()&&!node.ttsAudioUrl)return `${module.name} / ${node.name}需要讲解文案或讲解音频。`}}
  return null
}

async function publish(): Promise<void> {
  if (!content) return
  const validation = validateDraft(content.modules)
  if (validation) return showToast(validation)
  try {
    await syncDraftNow()
    if (draftConflict) return
    content = await api.publish(content.modules, draftBaseVersion, draftRevision)
    const draft = await api.getDraft()
    draftBaseVersion = draft.baseContentVersion; draftRevision = draft.revision
    draftStatuses = draft.narrationAudioStatuses; draftConflict = ''; publishError = ''; dirty = false
    draftMutationSequence = 0; draftSyncedSequence = 0
    localStorage.removeItem(draftKey)
    versions = await api.contentVersions(); renderDashboard(); showToast(`版本 V${content.version} 发布成功。`)
  } catch (error) {
    if (!api.hasSession()) { renderLogin('登录已过期，请重新登录。'); return }
    if (handleDraftError(error)) return
    publishError = error instanceof Error ? error.message : '发布失败'
    activeView = 'content'; renderDashboard(); showToast('发布已阻止，请根据页面提示修复素材。')
  }
}

function scheduleDraftSync(): void {
  if (draftSyncTimer !== null) window.clearTimeout(draftSyncTimer)
  draftSyncTimer = window.setTimeout(() => {
    draftSyncTimer = null
    void syncDraftNow().catch(() => { /* visible error is handled centrally */ })
  }, 500)
}

async function syncDraftNow(): Promise<void> {
  if (!content || draftConflict) {
    if (draftConflict) throw new Error(draftConflict)
    return
  }
  if (draftSyncTimer !== null) { window.clearTimeout(draftSyncTimer); draftSyncTimer = null }
  if (draftSyncInFlight) return draftSyncInFlight
  const synchronize = async (): Promise<void> => {
    while (content && draftSyncedSequence < draftMutationSequence) {
      const targetSequence = draftMutationSequence
      const modules = structuredClone(content.modules)
      try {
        const snapshot = await api.saveDraft(draftBaseVersion, draftRevision, modules)
        draftBaseVersion = snapshot.baseContentVersion
        draftRevision = snapshot.revision
        draftStatuses = snapshot.narrationAudioStatuses
        draftSyncedSequence = targetSequence
        draftConflict = ''
        saveLocalDraft()
        void ttsController?.refreshEvaluation()
        refreshOpenTtsWorkspace()
      } catch (error) {
        handleDraftError(error)
        throw error
      }
    }
  }
  draftSyncInFlight = synchronize().finally(() => { draftSyncInFlight = null })
  return draftSyncInFlight
}

function refreshOpenTtsWorkspace(): void {
  const modal = document.querySelector<HTMLElement>('#node-modal')
  const module = content?.modules.find(item => item.id === editingModuleId)
  const node = module?.nodes.find(item => item.id === editingNodeId)
  if (modal && module && node) refreshTtsWorkspace(module, node, modal)
}

function handleDraftError(error: unknown): boolean {
  if (error instanceof ApiError && (error.status === 409 || error.code?.includes('conflict'))) {
    draftConflict = '当前内容已发生变化，请刷新后重新确认。'
    root.querySelector<HTMLButtonElement>('#publish')?.setAttribute('disabled', '')
    refreshOpenTtsWorkspace()
    showToast(draftConflict)
    return true
  }
  if (error instanceof Error) showToast(`草稿尚未同步：${error.message}`)
  return false
}

function applyDraftSnapshot(snapshot: ContentDraftSnapshot): void {
  if (!content) return
  content = { ...content, modules: snapshot.modules }
  draftBaseVersion = snapshot.baseContentVersion
  draftRevision = snapshot.revision
  draftStatuses = snapshot.narrationAudioStatuses
  draftConflict = ''
  draftMutationSequence++
  draftSyncedSequence = draftMutationSequence
}

function saveLocalDraft(): void {
  if (!content) return
  localStorage.setItem(draftKey, JSON.stringify({
    baseVersion: draftBaseVersion,
    serverRevision: draftRevision,
    modules: content.modules,
    savedAt: new Date().toISOString(),
  }))
}

function restoreDraft(published: PublishedContent, serverDraft: ContentDraftSnapshot): {content: PublishedContent; dirty: boolean; needsSync: boolean} {
  try {
    const local = JSON.parse(localStorage.getItem(draftKey) ?? 'null') as {baseVersion:number;serverRevision?:number;modules:ExhibitionModule[]}|null
    if (local?.baseVersion === published.version && Array.isArray(local.modules)) {
      const canMerge = local.serverRevision === serverDraft.revision || (serverDraft.revision === 0 && local.serverRevision === undefined)
      if (canMerge) {
        const needsSync = JSON.stringify(local.modules) !== JSON.stringify(serverDraft.modules)
        return { content: { ...published, modules: needsSync ? local.modules : serverDraft.modules }, dirty: true, needsSync }
      }
      localStorage.removeItem(draftKey)
    } else if (local) localStorage.removeItem(draftKey)
  } catch { localStorage.removeItem(draftKey) }
  const serverDirty = serverDraft.revision > 0
  return { content: { ...published, modules: serverDraft.modules }, dirty: serverDirty, needsSync: false }
}
function showToast(message:string):void { let toast=root.querySelector<HTMLDivElement>('#toast');if(!toast){toast=document.createElement('div');toast.id='toast';toast.className='toast';root.appendChild(toast)}toast.textContent=message;toast.classList.add('visible');window.setTimeout(()=>toast?.classList.remove('visible'),3600) }

async function start():Promise<void>{if(!api.hasSession())return renderLogin();try{const me=await api.me();username=me.username;await loadDashboard()}catch{renderLogin('登录已过期，请重新登录。')}}
void start()
