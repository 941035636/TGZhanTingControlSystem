import './style.css'
import { api, ApiError, resolveAssetUrl, type AssetKind, type ClientRuntimeStatus, type ContentAsset, type ContentDraftSnapshot, type ContentPublishReadiness, type ContentVersionSummary, type ExhibitionModule, type NarrationAudioCandidateEvaluation, type NarrationAudioDraftStatus, type NarrationNode, type NarrationRoute, type OperationalEvent, type PlaybackSessionStatus, type PublishedContent, type SystemReadiness, type TtsProviderDescriptor, type TtsSynthesisConfiguration, type UiElementOverride, type UiExperienceConfig } from './api'
import { bindingStatusLabel, jobStatusLabel, TtsWorkflowController, type TtsWorkflowApi } from './tts-workflow'
import { validateLedIdleMediaDimensions } from './ui-media-validation'
import { adminUiFeatures, normalizeAdminWorkspaceView, type AdminWorkspaceView } from './feature-flags'

const app = document.querySelector<HTMLDivElement>('#app')
if (!app) throw new Error('未找到管理页面根节点')
const root = app
let content: PublishedContent | null = null
let ttsProviders: TtsProviderDescriptor[] = []
let draftBaseVersion = 0
let draftRevision = 0
let draftStatuses: NarrationAudioDraftStatus[] = []
let publishReadiness: ContentPublishReadiness | null = null
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
let activeUiEditorDraft: UiExperienceConfig | null = null
let routes: NarrationRoute[] = []
let readiness: SystemReadiness | null = null
let versions: ContentVersionSummary[] = []
let operationEvents: OperationalEvent[] = []
let publishError = ''
let activeView: AdminWorkspaceView = 'content'
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
const formatSize = (bytes: number): string => bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(2)} 吉字节` : bytes >= 1024 ** 2 ? `${(bytes / 1024 ** 2).toFixed(1)} 兆字节` : `${Math.max(1, Math.round(bytes / 1024))} 千字节`
const formatVersion = (version: number): string => `第 ${version} 版`
const displayAccountName = (value: string): string => {
  if (value.toLowerCase() === 'admin') return '管理员'
  return /[A-Za-z]/.test(value) ? '管理账号' : value
}
const displayAssetName = (value: string | null | undefined, kind: AssetKind): string => {
  const name = (value ?? '').replace(/\.[^.]+$/, '').trim()
  return !name || /[A-Za-z]/.test(name) ? `${assetKindName(kind)}素材` : name
}
const displayRuntimeStatus = (value: string | null | undefined, ready: boolean): string => {
  const normalized = value?.trim().toLowerCase()
  const labels: Record<string, string> = {
    ready: '已就绪', online: '在线', offline: '离线', preparing: '正在准备', syncing: '正在同步',
    playing: '正在播放', paused: '已暂停', idle: '空闲', error: '异常', degraded: '受限可用',
  }
  return normalized && labels[normalized] ? labels[normalized] : ready ? '已就绪' : '未就绪'
}
const displayLogLevel = (value: string): string => ({ information: '信息', warning: '警告', error: '错误' })[value.toLowerCase()] ?? '记录'
const displayLogCategory = (value: string): string => ({ route: '讲解路线', content: '内容发布', playback: '讲解播放', tts: '语音合成' })[value.toLowerCase()] ?? '系统运行'
const displayLogAction = (value: string): string => ({
  save: '保存', delete: '删除', publish: '发布', rollback: '回滚', adoptcandidate: '采用候选语音',
  degradedstart: '受限启动', start: '开始', failed: '失败', nodeskippedafterfailure: '故障后跳过',
  pause: '暂停', resume: '继续', skip: '跳过', retry: '重试', stop: '终止', clientrecovered: '终端恢复',
  completed: '完成', recovered: '会话恢复', recoverydiscarded: '放弃恢复',
})[value.toLowerCase()] ?? '状态更新'
const displayMessage = (value: string): string => {
  const localized = value
    .replace(/\badmin\b/gi, '管理员')
    .replace(/\btouch-main\b/gi, '触控中控端')
    .replace(/\bled-main\b/gi, '大屏播放端')
    .replace(/\bTTS\b/gi, '语音合成')
    .replace(/\bLED\b/gi, '大屏播放端')
    .replace(/\bServer\b/gi, '服务端')
    .replace(/\bMeloTTS\b/gi, '本地语音合成服务')
    .replace(/\bSHA-?256\b/gi, '文件校验值')
    .replace(/\bAssetId\b/gi, '素材编号')
    .replace(/\bMediaType\b/gi, '媒体类型')
    .replace(/\bURL\b/gi, '资源地址')
    .replace(/\bV(\d+)\b/g, '第 $1 版')
    .replace(/adopted a generated narration audio candidate\.?/gi, '采用了一条候选讲解语音。')
    .replace(/published content\s+第\s*(\d+)\s*版\.?/gi, '发布了第 $1 版内容。')
    .replace(/[A-Za-z][A-Za-z0-9_.:/\\-]*/g, '')
    .replace(/\s{2,}/g, ' ')
    .trim()
  return /[\u3400-\u9fff]/.test(localized) ? localized : '系统已记录一条运行信息。'
}

function renderLogin(message = ''): void {
  root.innerHTML = `<main class="login-shell"><section class="login-brand"><p class="eyebrow">展厅智能中控</p><h1>展厅自动讲解<br/>管理平台</h1><p>统一管理 12 个展陈模块、大屏视频、展示图片与内容发布。</p><div class="brand-stats"><span>双屏联动</span><span>素材管理</span><span>内容发布</span></div></section><section class="login-panel"><form id="login-form" class="login-card"><h2>欢迎登录</h2><p class="login-tip">请输入具有内容发布权限的管理账号</p><label>用户名<input id="username" autocomplete="username" placeholder="请输入用户名" /></label><label>密码<input id="password" type="password" autocomplete="current-password" placeholder="请输入密码" /></label><p id="login-error" class="login-error">${escapeHtml(message)}</p><button class="primary login-button" type="submit">登录管理平台</button><p class="login-foot">展厅智能中控系统 · 内部管理入口</p></form></section></main>`
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
    const [published, serverDraft, clients, sessions, ui, routeResult, ready, history, events] = await Promise.all([
      api.getContent(), api.getDraft(), api.clientStatuses(), api.playbackSessions(), api.getUi(), api.routes(), api.readiness(), api.contentVersions(), api.operations()
    ])
    const restored = restoreDraft(published, serverDraft)
    content = restored.content; clientStatuses = clients; playbackSessions = sessions; uiConfig = ui
    draftBaseVersion = serverDraft.baseContentVersion; draftRevision = serverDraft.revision; publishReadiness = serverDraft.publishReadiness
    routes = routeResult.routes ?? []; readiness = ready; versions = history; operationEvents = events; dirty = restored.dirty
    draftMutationSequence = restored.needsSync ? 1 : 0; draftSyncedSequence = 0; renderDashboard()
    if (restored.needsSync) void syncDraftNow()
  } catch (error) { root.innerHTML = `<main class="error"><h1>无法连接管理服务</h1><p>${escapeHtml(error instanceof Error ? error.message : String(error))}</p></main>` }
}

function renderDashboard(): void {
  if (!content) return
  activeView = normalizeAdminWorkspaceView(activeView)
  root.innerHTML = `<header class="topbar"><div><p class="eyebrow">展厅智能中控</p><h1>展厅自动讲解管理平台</h1><p class="meta">正式内容 ${formatVersion(content.version)} · 草稿第 ${draftRevision} 次修订 · ${content.modules.length} 个模块 · 发布人 ${escapeHtml(displayAccountName(content.publishedBy || '系统初始化'))}</p></div><div class="actions"><div class="account"><span>当前账号</span><strong>${escapeHtml(displayAccountName(username))}</strong></div><button id="logout">退出</button><button id="publish" class="primary" ${dirty && !draftConflict && publishReadiness?.canPublish === true ? '' : 'disabled'}>发布新版本</button></div></header>
    <nav class="workspace-nav">${navButton('content','内容中心')}${adminUiFeatures.enableCustomRouteUi ? navButton('routes','讲解路线') : ''}${navButton('versions','发布与回滚')}${navButton('operations','终端与运行')}</nav>
    <main>${renderWorkspace()}</main><div id="toast" class="toast" aria-live="polite"></div>`
  root.querySelector('#publish')?.addEventListener('click', publish)
  root.querySelector('#logout')?.addEventListener('click', async () => { await api.logout(); content = null; username = ''; renderLogin() })
  root.querySelectorAll<HTMLButtonElement>('[data-view]').forEach(button => button.addEventListener('click',()=>{activeView=normalizeAdminWorkspaceView(button.dataset.view as AdminWorkspaceView);renderDashboard()}))
  if(activeView==='content'){
    root.querySelector('#add')?.addEventListener('click', addModule)
    root.querySelector('#edit-ui')?.addEventListener('click', () => openUiEditor())
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
  return `${draftConflict?`<section class="publish-validation-error"><strong>草稿同步冲突</strong><p>${escapeHtml(draftConflict)}</p></section>`:''}${publishError?`<section class="publish-validation-error"><strong>发布已阻止：请修复以下素材</strong><p>${escapeHtml(displayMessage(publishError)).replaceAll('；','<br/>')}</p></section>`:''}${renderContentPublishSummary()}<section class="summary"><article><strong>${content.modules.filter(item => item.enabled).length}</strong><span>已启用模块</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.length, 0)}</strong><span>展示节点</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.flatMap(node => node.assets).filter(asset => asset.kind === 0 || asset.kind === 1).length, 0)}</strong><span>视频与图片</span></article><article><strong>${dirty ? '待发布' : '已同步'}</strong><span>编辑状态</span></article><article><strong>${clientStatuses.filter(client => client.online).length}/${Math.max(2, clientStatuses.length)}</strong><span>终端在线</span></article><article><strong>${playbackSessions.length}</strong><span>进行中讲解</span></article></section>
  <section class="section-title"><div><p class="eyebrow">内容管理</p><h2>内容中心</h2><p class="section-note">配置模块及大屏视频、图片素材；修改会自动保存到服务器草稿，发布新版本后终端才会更新。</p></div><div class="section-actions"><button id="edit-ui">终端界面设置</button><button id="add">新增模块</button></div></section><section class="module-grid">${content.modules.sort((a,b) => a.order-b.order).map(moduleCard).join('')}</section>`
}
const displayLanguage = (value: string): string => value.toLowerCase() === 'zh-cn' ? '中文（中国大陆）' : '当前配置语言'
const displayProvider = (provider: TtsProviderDescriptor | undefined): string => provider?.developmentOnly ? '开发测试语音服务' : '本地中文语音服务'
const displayVoice = (provider: TtsProviderDescriptor | undefined, voiceName: string): string =>
  provider?.developmentOnly ? '开发测试音色' : /^[\u0000-\u007f]+$/.test(voiceName) ? '中文标准讲解' : voiceName

function renderContentPublishSummary(): string {
  if (!publishReadiness) return '<section id="content-publish-summary" class="narration-publish-summary checking"><strong>正在由服务器检查内容发布状态…</strong></section>'
  const relevantIssues = publishReadiness.issues.filter(issue => issue.code !== 'video_without_narration_audio')
  const issues = relevantIssues.map(issue => `<li class="${issue.severity === 1 ? 'blocking' : 'warning'}"><strong>${issue.severity === 1 ? '阻止发布' : '发布提醒'}</strong><span>${escapeHtml(displayMessage(issue.message))}</span></li>`).join('')
  const blocking = publishReadiness.issues.filter(issue => issue.severity === 1).length
  return `<section id="content-publish-summary" class="narration-publish-summary ${publishReadiness.canPublish ? 'ready' : 'blocked'}"><header><div><span>内容发布检查</span><strong>${publishReadiness.canPublish ? '可以发布' : `存在 ${blocking} 项阻塞问题`}</strong></div><p>结论来自服务端，发布时会再次执行素材完整性校验。</p></header>${issues ? `<ul>${issues}</ul>` : '<p class="publish-ready-note">当前草稿已通过发布检查。</p>'}</section>`
}

function renderRoutesView():string{
  if(!content)return ''
  const cards=routes.length?routes.map(route=>{
    const modules=route.moduleIds.map(id=>content!.modules.find(item=>item.id===id)).filter((item):item is ExhibitionModule=>Boolean(item))
    const seconds=modules.reduce((total,module)=>total+module.nodes.reduce((sum,node)=>sum+Math.max(0,...node.assets.map(asset=>asset.durationSeconds||0)),0),0)
    return `<article class="management-card"><div class="card-kicker">${modules.length} 个主题${seconds>0?` · 约 ${Math.ceil(seconds/60)} 分钟`:''}</div><h3>${escapeHtml(route.name)}</h3><p>${escapeHtml(modules.map(item=>item.name).join(' → ')||'路线中的主题已失效')}</p><footer><span>更新于 ${formatTime(route.updatedAtUtc)}</span><button data-edit-route="${route.id}">编辑</button><button data-delete-route="${route.id}" class="danger-text">删除</button></footer></article>`
  }).join(''):'<div class="empty-management"><strong>尚未创建常用路线</strong><p>创建后，中控接待首页可以直接一键开始讲解。</p></div>'
  return `${readinessBanner()}<section class="section-title"><div><p class="eyebrow">路线管理</p><h2>讲解路线</h2><p class="section-note">统一维护正式接待路线；中控端仍可建立临时组合。</p></div><div class="section-actions"><button id="add-route" class="primary">新增路线</button></div></section><section class="management-grid">${cards}</section>`
}

function bindRouteManagement():void{
  if(!adminUiFeatures.enableCustomRouteUi){activeView='content';renderDashboard();return}
  root.querySelector('#add-route')?.addEventListener('click',()=>openRouteEditor(null))
  root.querySelectorAll<HTMLElement>('[data-edit-route]').forEach(button=>button.addEventListener('click',()=>openRouteEditor(routes.find(item=>item.id===button.dataset.editRoute)??null)))
  root.querySelectorAll<HTMLElement>('[data-delete-route]').forEach(button=>button.addEventListener('click',async()=>{
    const route=routes.find(item=>item.id===button.dataset.deleteRoute);if(!route||!window.confirm(`确定删除路线“${route.name}”吗？`))return
    try{await api.deleteRoute(route.id);routes=(await api.routes()).routes;renderDashboard();showToast('路线已删除。')}catch(error){showToast(error instanceof Error?error.message:'删除路线失败')}
  }))
}

function openRouteEditor(route:NarrationRoute|null):void{
  if(!adminUiFeatures.enableCustomRouteUi){activeView='content';renderDashboard();showToast('该功能暂未开放。');return}
  if(!content)return
  const modal=document.createElement('div');modal.className='modal-backdrop';modal.id='route-modal'
  modal.innerHTML=`<section class="route-editor-modal"><header class="editor-header"><div><p class="eyebrow">路线编辑</p><h2>${route?'编辑讲解路线':'新建讲解路线'}</h2></div><button id="close-route" class="icon-button">×</button></header><div class="route-editor-body"><label>路线名称<input id="route-name" maxlength="30" value="${escapeHtml(route?.name??'')}" placeholder="例如：重要客户接待路线"/></label><strong>按讲解顺序选择主题</strong><p class="section-note">点击主题加入路线；使用箭头调整顺序。</p><div id="route-order" class="route-order"></div><div class="route-module-picker">${content.modules.filter(item=>item.enabled).sort((a,b)=>a.order-b.order).map(item=>`<button data-pick-module="${item.id}"><span>${String(item.order).padStart(2,'0')}</span>${escapeHtml(item.name)}</button>`).join('')}</div><footer class="editor-footer"><span>保存后中控端会自动刷新常用路线。</span><button id="cancel-route">取消</button><button id="save-route" class="primary">保存路线</button></footer></div></section>`
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
  const rows=versions.map(item=>`<article class="version-row ${item.current?'current':''}"><div><strong>${formatVersion(item.version)}${item.current?' · 当前版本':''}</strong><p>${item.moduleCount} 个模块 · ${item.nodeCount} 个节点 · ${escapeHtml(displayAccountName(item.publishedBy))}</p></div><time>${formatTime(item.publishedAtUtc)}</time><button data-rollback="${item.version}" ${item.current?'disabled':''}>回滚到此版本</button></article>`).join('')
  return `<section class="section-title"><div><p class="eyebrow">版本管理</p><h2>发布与回滚</h2><p class="section-note">回滚不会覆盖历史，而是使用旧内容生成一个新的正式版本。</p></div></section><section class="version-list">${rows||'<div class="empty-management">暂无发布历史</div>'}</section>`
}

function bindVersionManagement():void{
  root.querySelectorAll<HTMLElement>('[data-rollback]').forEach(button=>button.addEventListener('click',async()=>{const version=Number(button.dataset.rollback);if(!content||!window.confirm(`确定将正式内容回滚到${formatVersion(version)}吗？`))return;try{content=await api.rollbackContent(version,content.version,draftRevision);const draft=await api.getDraft();draftBaseVersion=draft.baseContentVersion;draftRevision=draft.revision;draftStatuses=draft.narrationAudioStatuses;publishReadiness=draft.publishReadiness;draftConflict='';dirty=false;draftMutationSequence=0;draftSyncedSequence=0;localStorage.removeItem(draftKey);versions=await api.contentVersions();renderDashboard();showToast(`已回滚并生成新版本：${formatVersion(content.version)}。`)}catch(error){showToast(error instanceof Error?error.message:'回滚失败')}}))
}

function renderOperationsView():string{
  const clients=clientStatuses.map(client=>`<article class="terminal-card ${client.online&&client.ready?'ready':''}"><div><strong>${client.kind===1?'大屏播放端':'触控中控端'}</strong><span>${client.online?'在线':'离线'}</span></div><p>终端状态 · ${client.ready?'已就绪':'未就绪'}</p><p>内容${formatVersion(client.contentVersion??0)} · ${escapeHtml(displayRuntimeStatus(client.status,client.ready))}</p><small>最后心跳 ${formatTime(client.lastSeenUtc)}</small></article>`).join('')
  const sessions=playbackSessions.length?playbackSessions.map(item=>`<article class="session-row"><strong>${escapeHtml(item.moduleName)} / ${escapeHtml(item.nodeName)}</strong><span>${item.paused?'已暂停':item.playPublished?'正在讲解':`准备中 ${Math.round((item.preparationProgress||0)*100)}%`}</span><small>${item.currentNodeNumber}/${item.totalNodes} · 内容${formatVersion(item.contentVersion)}</small></article>`).join(''):'<div class="empty-inline">当前没有进行中的讲解</div>'
  const logs=operationEvents.map(item=>`<tr><td>${formatTime(item.occurredAtUtc)}</td><td><span class="log-level ${item.level.toLowerCase()}">${escapeHtml(displayLogLevel(item.level))}</span></td><td>${escapeHtml(displayLogCategory(item.category))} / ${escapeHtml(displayLogAction(item.action))}</td><td>${escapeHtml(displayMessage(item.message))}</td></tr>`).join('')
  return `${readinessBanner()}<section class="section-title"><div><p class="eyebrow">运行状态</p><h2>终端与运行</h2><p class="section-note">查看终端在线、内容版本、活动讲解和关键操作记录。</p></div><button id="refresh-operations">刷新状态</button></section><section class="terminal-grid">${clients||'<div class="empty-management">尚无终端注册</div>'}</section><h3 class="subheading">活动讲解</h3><section class="session-list">${sessions}</section><h3 class="subheading">运行日志</h3><div class="log-table-wrap"><table class="log-table"><thead><tr><th>时间</th><th>级别</th><th>分类</th><th>事件</th></tr></thead><tbody>${logs||'<tr><td colspan="4">暂无运行日志</td></tr>'}</tbody></table></div>`
}

function readinessBanner():string{const state=!readiness?.canStart?'blocked':readiness.ledReady?'ready':'degraded';const label=state==='ready'?'✓ 系统可以接待':state==='degraded'?'! 系统受限可用':'! 系统暂未就绪';return `<section class="readiness-banner ${state}"><div><span>${label}</span><strong>${escapeHtml(displayMessage(readiness?.message??'正在读取系统状态'))}</strong></div><p>服务端内容${formatVersion(readiness?.contentVersion??0)} · 大屏内容${formatVersion(readiness?.ledContentVersion??0)}</p></section>`}

async function refreshOperations():Promise<void>{try{[clientStatuses,playbackSessions,readiness,operationEvents]=await Promise.all([api.clientStatuses(),api.playbackSessions(),api.readiness(),api.operations()]);renderDashboard();showToast('运行状态已刷新。')}catch(error){showToast(error instanceof Error?error.message:'刷新失败')}}
function formatTime(value:string):string{const date=new Date(value);return Number.isNaN(date.getTime())?'-':date.getUTCFullYear()<2000?'系统初始化':date.toLocaleString('zh-CN',{hour12:false})}

function moduleCard(module: ExhibitionModule): string { return `<article class="module-card ${module.enabled ? '' : 'disabled'}" data-module="${module.id}"><div class="module-number">${String(module.order).padStart(2,'0')}</div><div class="module-body"><div class="module-heading"><input class="module-name" value="${escapeHtml(module.name)}" aria-label="模块名称"/><label class="switch"><input type="checkbox" class="module-enabled" ${module.enabled?'checked':''}/><span></span></label></div><textarea class="module-description" placeholder="填写模块简介">${escapeHtml(module.description)}</textarea><div class="module-cover-setting"><span>${module.coverUrl?'已设置中控卡片底图':'尚未设置中控卡片底图'}</span><label class="upload-button">${module.coverUrl?'替换底图':'上传底图'}<input class="module-cover-file" type="file" accept="image/*"/></label></div><div class="module-footer"><span>${module.nodes.length} 个展示节点 · ${module.galleryImages.length} 张图集图片</span><div class="module-actions"><button class="gallery-button">管理图片图集</button><button class="node-button">编辑大屏素材</button></div></div></div></article>` }

function bindCard(card: HTMLElement): void {
  const module = content?.modules.find(item => item.id === card.dataset.module); if (!module) return
  card.querySelector<HTMLInputElement>('.module-name')?.addEventListener('input', event => { module.name=(event.target as HTMLInputElement).value; markDirty() })
  card.querySelector<HTMLTextAreaElement>('.module-description')?.addEventListener('input', event => { module.description=(event.target as HTMLTextAreaElement).value; markDirty() })
  card.querySelector<HTMLInputElement>('.module-enabled')?.addEventListener('change', event => { module.enabled=(event.target as HTMLInputElement).checked; markDirty(true) })
  card.querySelector<HTMLInputElement>('.module-cover-file')?.addEventListener('change', async event => {
    const file=(event.target as HTMLInputElement).files?.[0];if(!file)return
    try{const asset=await api.uploadAsset(file,1,0,()=>{});module.coverUrl=asset.url;markDirty(true);await syncDraftNow();showToast('板块底图已上传，发布新版本后中控端生效。')}catch(error){showToast(error instanceof Error?error.message:'板块底图上传失败')}
  })
  card.querySelector('.gallery-button')?.addEventListener('click', () => openGalleryEditor(module.id))
  card.querySelector('.node-button')?.addEventListener('click', () => openNodeEditor(module.id))
}

function openGalleryEditor(moduleId: string): void {
  const module = content?.modules.find(item => item.id === moduleId); if (!module) return
  renderGalleryEditor(module)
}

function renderGalleryEditor(module: ExhibitionModule): void {
  document.querySelector('#gallery-modal')?.remove()
  const images = module.galleryImages
  const modal = document.createElement('div'); modal.id = 'gallery-modal'; modal.className = 'modal-backdrop'
  modal.innerHTML = `<section class="gallery-modal"><header class="editor-header"><div><p class="eyebrow">模块图片图集</p><h2>${escapeHtml(module.name)} · 图片管理</h2></div><button id="close-gallery" class="icon-button">×</button></header><div class="gallery-editor-body"><div class="gallery-toolbar"><div><strong>有序图片图集</strong><p>支持 JPG、JPEG、PNG；数组顺序就是后续大屏展示顺序。</p></div><label class="upload-button gallery-upload">批量上传图片<input id="gallery-files" type="file" multiple accept=".jpg,.jpeg,.png,image/jpeg,image/png"/></label></div><div id="gallery-upload-progress" class="gallery-upload-status"></div><div class="gallery-grid">${images.length ? images.map((asset,index) => galleryCard(asset,index,images.length)).join('') : '<div class="empty-gallery"><strong>当前模块还没有图集图片</strong><span>可以一次选择多张 JPG 或 PNG 图片上传。</span></div>'}</div><footer class="editor-footer"><span>排序、名称和删除操作会保存到内容草稿，发布新版本后进入正式素材清单。</span><button id="done-gallery" class="primary">完成编辑</button></footer></div></section>`
  document.body.appendChild(modal)
  const close = () => { modal.remove(); renderDashboard() }
  modal.querySelector('#close-gallery')?.addEventListener('click', close)
  modal.querySelector('#done-gallery')?.addEventListener('click', close)
  modal.addEventListener('click', event => { if (event.target === modal) close() })
  modal.querySelector<HTMLInputElement>('#gallery-files')?.addEventListener('change', event => void uploadGalleryFiles(module, (event.target as HTMLInputElement).files, modal))
  bindGalleryCards(module, modal)
}

function galleryCard(asset: ContentAsset, index: number, count: number): string {
  return `<article class="gallery-card" draggable="true" data-gallery-index="${index}"><div class="gallery-preview"><img src="${escapeHtml(resolveAssetUrl(asset.url))}" alt="${escapeHtml(asset.name)}" draggable="false"/><span>${String(index + 1).padStart(2,'0')}</span></div><label>图片名称<input class="gallery-name" value="${escapeHtml(asset.name)}" maxlength="80"/></label><p>${formatSize(asset.sizeBytes)} · ${escapeHtml(asset.mediaType ?? '图片')}</p><div class="gallery-card-actions"><button data-gallery-up ${index === 0 ? 'disabled' : ''}>上移</button><button data-gallery-down ${index === count - 1 ? 'disabled' : ''}>下移</button><a href="${escapeHtml(resolveAssetUrl(asset.url))}" target="_blank" rel="noreferrer">预览</a><button class="danger-text" data-gallery-remove>删除</button></div></article>`
}

function bindGalleryCards(module: ExhibitionModule, modal: HTMLElement): void {
  let draggedIndex: number | null = null
  modal.querySelectorAll<HTMLElement>('[data-gallery-index]').forEach(card => {
    const index = Number(card.dataset.galleryIndex)
    card.querySelector<HTMLInputElement>('.gallery-name')?.addEventListener('change', event => {
      module.galleryImages[index].name = (event.target as HTMLInputElement).value.trim() || `图集图片 ${index + 1}`
      markDirty()
    })
    card.querySelector('[data-gallery-up]')?.addEventListener('click', () => moveGalleryImage(module, index, index - 1))
    card.querySelector('[data-gallery-down]')?.addEventListener('click', () => moveGalleryImage(module, index, index + 1))
    card.querySelector('[data-gallery-remove]')?.addEventListener('click', () => {
      module.galleryImages.splice(index, 1); markDirty(); renderGalleryEditor(module)
    })
    card.addEventListener('dragstart', () => { draggedIndex = index; card.classList.add('dragging') })
    card.addEventListener('dragend', () => { draggedIndex = null; card.classList.remove('dragging') })
    card.addEventListener('dragover', event => { event.preventDefault(); card.classList.add('drag-target') })
    card.addEventListener('dragleave', () => card.classList.remove('drag-target'))
    card.addEventListener('drop', event => {
      event.preventDefault(); card.classList.remove('drag-target')
      if (draggedIndex !== null && draggedIndex !== index) moveGalleryImage(module, draggedIndex, index)
    })
  })
}

function moveGalleryImage(module: ExhibitionModule, from: number, to: number): void {
  if (to < 0 || to >= module.galleryImages.length || from === to) return
  const [asset] = module.galleryImages.splice(from, 1); module.galleryImages.splice(to, 0, asset)
  markDirty(); renderGalleryEditor(module)
}

async function uploadGalleryFiles(module: ExhibitionModule, files: FileList | null, modal: HTMLElement): Promise<void> {
  const selected = Array.from(files ?? [])
  if (!selected.length) return
  const unsupported = selected.find(file => !/\.(jpe?g|png)$/i.test(file.name) || (Boolean(file.type) && !/^image\/(jpeg|png)$/i.test(file.type)))
  if (unsupported) { showToast(`不支持“${unsupported.name}”，图集仅允许 JPG、JPEG、PNG。`); return }
  const status = modal.querySelector<HTMLElement>('#gallery-upload-progress')
  try {
    for (let index = 0; index < selected.length; index++) {
      const file = selected[index]
      if (status) status.textContent = `正在上传 ${index + 1}/${selected.length}：${file.name}`
      const asset = await api.uploadAsset(file, 1, 0, percent => { if(status) status.textContent = `正在上传 ${index + 1}/${selected.length}：${file.name} · ${percent}%` })
      module.galleryImages.push(asset)
    }
    markDirty(); await syncDraftNow(); showToast(`已上传 ${selected.length} 张图集图片。`); renderGalleryEditor(module)
  } catch (error) {
    if (status) status.textContent = ''
    showToast(error instanceof Error ? error.message : '图集图片上传失败')
  }
}

function openNodeEditor(moduleId: string): void {
  const module = content?.modules.find(item => item.id === moduleId); if (!module) return
  editingModuleId = moduleId
  if (!module.nodes.some(node => node.id === editingNodeId)) editingNodeId = module.nodes.sort((a,b)=>a.order-b.order)[0]?.id ?? null
  renderNodeEditor()
}

function renderNodeEditor(): void {
  document.querySelector('#node-modal')?.remove()
  const module = content?.modules.find(item => item.id === editingModuleId); if (!module) return
  const nodes = module.nodes.sort((a,b)=>a.order-b.order)
  const node = nodes.find(item => item.id === editingNodeId) ?? null
  const modal = document.createElement('div'); modal.id = 'node-modal'; modal.className = 'modal-backdrop'
  modal.innerHTML = `<section class="editor-modal"><header class="editor-header"><div><p class="eyebrow">大屏内容编辑</p><h2>${escapeHtml(module.name)} · 展示素材</h2></div><button id="close-editor" class="icon-button">×</button></header><div class="editor-layout"><aside class="node-list"><div class="node-list-title"><strong>展示节点</strong><button id="add-node">＋ 新增</button></div>${nodes.length ? nodes.map(item => `<button class="node-list-item ${item.id===editingNodeId?'active':''}" data-node-id="${item.id}"><span>${String(item.order).padStart(2,'0')}</span><strong>${escapeHtml(item.name || '未命名节点')}</strong><small>${item.assets.filter(asset => asset.kind === 0 || asset.kind === 1).length} 个视频/图片</small></button>`).join('') : '<div class="empty-node">暂无展示节点<br/>点击“新增”创建第一段内容</div>'}</aside><div class="node-editor">${node ? nodeForm(node) : '<div class="empty-editor"><strong>尚未创建展示节点</strong><p>创建节点后，可上传大屏视频或图片素材。</p></div>'}</div></div></section>`
  document.body.appendChild(modal)
  modal.querySelector('#close-editor')?.addEventListener('click', closeNodeEditor)
  modal.addEventListener('click', event => { if (event.target === modal) closeNodeEditor() })
  modal.querySelector('#add-node')?.addEventListener('click', () => addNode(module))
  modal.querySelectorAll<HTMLElement>('[data-node-id]').forEach(button => button.addEventListener('click', () => { editingNodeId=button.dataset.nodeId!; renderNodeEditor() }))
  if (node) { bindNodeForm(module, node, modal) }
}

function nodeForm(node: NarrationNode): string {
  const displayAssets = node.assets.filter(asset => asset.kind === 0 || asset.kind === 1)
  return `<div class="editor-fields"><div class="segment-guidance"><strong>按大屏展示顺序编排内容</strong><p>每个节点可维护视频或图片素材。修改会自动保存到草稿，发布新版本后才会同步到终端。</p></div><div class="field-row"><label>节点名称<input id="node-name" value="${escapeHtml(node.name)}"/></label><label class="short-field">顺序<input id="node-order" type="number" min="1" value="${node.order}"/></label><label class="short-field">故障策略<select id="failure-policy"><option value="0" ${node.failurePolicy===0?'selected':''}>跳过继续</option><option value="1" ${node.failurePolicy===1?'selected':''}>停止讲解</option></select></label></div>
    <section class="assets-section visual-assets-only"><div class="assets-heading"><div><strong>大屏展示素材</strong><p>支持视频与图片。建议使用 1920×1080 横屏素材；超大视频将直接流式写入服务器磁盘。</p></div><span>${displayAssets.length} 个素材</span></div><div class="upload-row"><label>素材类型<select id="asset-kind"><option value="0">宣传视频</option><option value="1">展示图片</option></select></label><label>展示时长（秒）<input id="asset-duration" type="number" min="0" step="0.1" value="0"/><small>图片可填写展示时长；视频可保持 0</small></label><label class="upload-button">选择并上传<input id="asset-file" type="file" accept="video/*,image/*,.mov,.mkv,.webm"/></label><div id="upload-progress" class="upload-progress"><span></span></div></div><div class="asset-list">${displayAssets.length ? displayAssets.map(asset => `<article><div class="asset-icon">${asset.kind===0?'▶':'▧'}</div><div><strong>${escapeHtml(displayAssetName(asset.name, asset.kind))}</strong><p>${assetKindName(asset.kind)} · ${formatSize(asset.sizeBytes)}${asset.durationSeconds?` · ${asset.durationSeconds} 秒`:''}</p></div><a href="${escapeHtml(asset.url)}" target="_blank" rel="noreferrer">查看</a><button data-remove-asset="${asset.id}">移除</button></article>`).join('') : '<div class="empty-assets"><strong>尚未上传大屏素材</strong><span>请选择视频或图片并上传</span></div>'}</div></section>
    <footer class="editor-footer"><span>修改保存在当前草稿中，点击主页面“发布新版本”后终端才会更新。</span><button id="delete-node" class="danger">删除当前节点</button><button id="done-node" class="primary">完成编辑</button></footer></div>`
}

function ttsPanel(node: NarrationNode): string {
  const adopted = node.narrationAudio
  const currentAudioUrl = adopted?.asset.url ?? node.ttsAudioUrl
  const currentAudio = currentAudioUrl
    ? `<article class="tts-current-audio"><div><span class="tts-card-label">当前已绑定讲解音频</span><strong>${escapeHtml(displayAssetName(adopted?.asset.name ?? currentAudioUrl.split('/').pop(), 3))}</strong><p>${adopted ? `完整资产 · ${formatSize(adopted.asset.sizeBytes)}` : '历史音频'}</p></div><a href="${escapeHtml(resolveAssetUrl(currentAudioUrl))}" target="_blank" rel="noreferrer">打开音频</a><button id="remove-audio" class="danger-text">移除</button></article>`
    : '<div class="tts-current-empty">当前尚未绑定讲解音频</div>'
  return `<div class="tts-heading"><div><strong>讲解音频</strong><p>当前不使用自动语音合成。需要语音时，请上传人工录制的讲解音频。</p></div><span class="status-pill">手工上传</span></div>${currentAudio}<div class="tts-manual-upload"><div><strong>上传讲解音频</strong><p>音频会进入不可变素材体系，并随内容版本发布到播放端。</p></div><label class="upload-audio">选择音频<input id="audio-file" type="file" accept="audio/*,.mp3,.wav,.m4a,.aac"/></label></div>`
}

function legacyTtsPanel(node: NarrationNode): string {
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
  const currentOrigin = adopted ? ['语音合成生成', '人工上传', '旧版语音'][adopted.origin] : node.ttsAudioUrl ? '旧版语音' : '未配置'
  const providerOptions = availableProviders.map(provider =>
    `<option value="${escapeHtml(provider.providerId)}" ${configuration?.providerKey === provider.providerId ? 'selected' : ''}>${escapeHtml(displayProvider(provider))}${provider.developmentOnly ? '（开发测试）' : ''}</option>`).join('')
  const voiceOptions = (selectedProvider?.voices ?? []).map(voice =>
    `<option value="${escapeHtml(voice.voiceId)}" ${configuration?.voice === voice.voiceId ? 'selected' : ''}>${escapeHtml(displayVoice(selectedProvider ?? undefined, voice.displayName))} · ${escapeHtml(displayLanguage(voice.language))}</option>`).join('')
  const providersUnavailable = !availableProviders.length
    ? `<div class="tts-empty"><strong>当前未配置语音合成服务</strong><p>生成操作已禁用；仍可继续使用下方“上传讲解音频”。</p>${ttsProviders.filter(item => !item.available).map(item => `<small>${escapeHtml(displayProvider(item))}：当前不可用，请检查本地语音运行环境。</small>`).join('')}</div>`
    : ''
  const developmentWarning = selectedProvider?.developmentOnly
    ? '<div class="tts-development-note">当前选择的是开发测试音色，仅用于流程验证，不代表正式商用语音效果。</div>'
    : ''
  const currentAudio = currentAudioUrl
    ? `<article class="tts-current-audio"><div><span class="tts-card-label">${draftRevision > 0 ? '当前草稿已采用语音 · 等待发布' : '当前已发布语音'}</span><strong>${escapeHtml(displayAssetName(adopted?.asset.name ?? currentAudioUrl.split('/').pop(), 3))}</strong><p>${escapeHtml(currentOrigin)}${adopted ? ` · ${formatSize(adopted.asset.sizeBytes)} · ${escapeHtml(displayVoice(ttsProviders.find(item => item.providerId === adopted.synthesisConfiguration.providerKey), adopted.synthesisConfiguration.voice || '人工音频'))}` : ''}</p></div><a href="${escapeHtml(resolveAssetUrl(currentAudioUrl))}" target="_blank" rel="noreferrer">打开音频</a><button id="remove-audio" class="danger-text">移除</button></article>`
    : '<div class="tts-current-empty">当前尚未采用讲解语音</div>'
  const job = workflow?.job
  const jobView = job
    ? `<div class="tts-job ${job.status === 3 ? 'error' : job.status === 2 ? 'success' : ''}"><span>${jobStatusLabel(job.status)}</span><small>任务创建于 ${formatTime(job.createdAtUtc)}${job.retryCount ? ` · 已重试 ${job.retryCount} 次` : ''}</small>${job.status === 0 || job.status === 1 ? '<button id="cancel-tts">取消生成</button>' : ''}</div>`
    : ''
  const draftPending = draftSyncedSequence < draftMutationSequence
  const candidateView = candidate
    ? `<article class="tts-candidate"><header><div><span class="tts-card-label">待采用的候选语音</span><strong>${escapeHtml(displayVoice(ttsProviders.find(item => item.providerId === candidate.providerId), candidate.voice))}</strong></div><span class="status-pill ${evaluation?.adoptable ? 'fresh' : 'warning'}">${evaluation?.adoptable ? '可以采用' : '需要重新确认'}</span></header><dl><div><dt>语音服务</dt><dd>${escapeHtml(displayProvider(ttsProviders.find(item => item.providerId === candidate.providerId)))}</dd></div><div><dt>生成时间</dt><dd>${formatTime(candidate.createdAtUtc)}</dd></div><div><dt>时长</dt><dd>${candidate.validation.durationSeconds > 0 ? `${candidate.validation.durationSeconds.toFixed(1)} 秒` : '未提供'}</dd></div><div><dt>匹配检查</dt><dd>${evaluation ? `${evaluation.narrationTextMatches ? '文案一致' : '文案已变化'} · ${evaluation.synthesisConfigurationMatches ? '配置一致' : '配置已变化'}` : '正在由服务器检查'}</dd></div></dl>${evaluation && !evaluation.adoptable ? `<p class="candidate-warning">${escapeHtml(candidateEvaluationMessage(evaluation))}</p>` : ''}<footer><button id="preview-candidate">${workflow?.previewing ? '暂停试听' : '播放试听'}</button><button id="replay-candidate">重新播放</button><button id="abandon-candidate">放弃候选</button><button id="adopt-candidate" class="primary" ${evaluation?.adoptable && !workflow?.adopting && !draftConflict && !draftPending ? '' : 'disabled'}>${workflow?.adopting ? '正在采用…' : '采用到草稿'}</button></footer></article>`
    : ''
  const workflowError = workflow?.error ? `<div class="tts-error">${escapeHtml(workflow.error)}</div>` : ''
  const unsynced = draftPending
    ? '<div class="tts-sync-note">草稿变更正在提交服务器，状态检查完成后才能采用候选语音。</div>'
    : ''
  const rateSupported = selectedProvider?.capabilities.supportsRate !== false
  const pitchSupported = selectedProvider?.capabilities.supportsPitch !== false
  return `<div class="tts-heading"><div><strong>讲解语音</strong><p>生成、试听和采用彼此独立；采用后仍需发布才会进入正式内容。</p></div><span class="status-pill status-${statusValue}">${bindingStatusLabel(statusValue)}</span></div>
    ${statusValue !== 1 ? `<p class="tts-status-message">${bindingStatusDescription(statusValue)}</p>` : ''}
    ${currentAudio}
    <div class="tts-config-grid"><label>语音服务<select id="tts-provider" ${providersUnavailable ? 'disabled' : ''}>${providerOptions}</select></label><label>音色<select id="tts-voice" ${providersUnavailable ? 'disabled' : ''}>${voiceOptions}</select></label><label>语言<input id="tts-language" value="${escapeHtml(displayLanguage(configuration?.language ?? selectedVoice?.language ?? 'zh-CN'))}" readonly/></label><label>语速<input id="tts-rate" type="number" step="0.05" min="${selectedProvider?.capabilities.minRate ?? 0.5}" max="${selectedProvider?.capabilities.maxRate ?? 2}" value="${configuration?.rate ?? 1}" ${providersUnavailable || !rateSupported ? 'disabled' : ''}/>${rateSupported ? '' : '<small>当前音色不支持语速调整</small>'}</label><label>音调<input id="tts-pitch" type="number" step="0.05" min="${selectedProvider?.capabilities.minPitch ?? 0}" max="${selectedProvider?.capabilities.maxPitch ?? 0}" value="${configuration?.pitch ?? 0}" ${providersUnavailable || !pitchSupported ? 'disabled' : ''}/>${pitchSupported ? '' : '<small>当前音色不支持音调调整</small>'}</label><button id="generate-tts" class="primary" ${providersUnavailable || !node.narrationText.trim() || workflow?.generating || Boolean(draftConflict) ? 'disabled' : ''}>${workflow?.generating ? '正在生成…' : '生成候选语音'}</button></div>
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
    rate: provider.capabilities.supportsRate === false ? 1 : existingProvider && existingVoice ? node.ttsConfiguration?.rate ?? 1 : 1,
    pitch: provider.capabilities.supportsPitch === false ? 0 : existingProvider && existingVoice ? node.ttsConfiguration?.pitch ?? 0 : 0,
    volume: provider.capabilities.supportsVolume === false ? 1 : existingProvider && existingVoice ? node.ttsConfiguration?.volume ?? 1 : 1,
    outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav',
    sampleRateHz: provider.capabilities.defaultSampleRateHz || (existingProvider && existingVoice ? node.ttsConfiguration?.sampleRateHz : 0) || 24000,
    channels: provider.capabilities.defaultChannels || (existingProvider && existingVoice ? node.ttsConfiguration?.channels : 0) || 1,
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
    rate: provider.capabilities.supportsRate === false ? 1 : Math.min(provider.capabilities.maxRate, Math.max(provider.capabilities.minRate, rate)),
    pitch: provider.capabilities.supportsPitch === false ? 0 : Math.min(provider.capabilities.maxPitch, Math.max(provider.capabilities.minPitch, pitch)),
    volume: provider.capabilities.supportsVolume === false ? 1 : node.ttsConfiguration?.volume ?? 1,
    outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav',
    sampleRateHz: provider.capabilities.defaultSampleRateHz || node.ttsConfiguration?.sampleRateHz || 24000,
    channels: provider.capabilities.defaultChannels || node.ttsConfiguration?.channels || 1,
  }
}

function initializeTtsController(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  ttsController = new TtsWorkflowController(ttsWorkflowApi, module.id, node.id, state => {
    if (state.providersLoaded) ttsProviders = [...state.providers]
    refreshTtsWorkspace(module, node, modal)
  })
  void ttsController.loadProviders()
}

// Automatic TTS generation is disabled for the current product deployment.
// Keep the legacy workflow code available for data compatibility without
// allowing it to become an unused-build error or an active UI path.
void legacyTtsPanel
void initializeTtsController
void disposeNodeRuntime

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
    node.ttsConfiguration = { providerKey: provider.providerId, voice: voice.voiceId, language: voice.language, rate: 1, pitch: 0, volume: 1, outputMediaType: provider.capabilities.supportedMediaTypes[0] ?? 'audio/wav', sampleRateHz: provider.capabilities.defaultSampleRateHz || 24000, channels: provider.capabilities.defaultChannels || 1 }
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
  modal.querySelector('#done-node')?.addEventListener('click', closeNodeEditor)
  modal.querySelector('#delete-node')?.addEventListener('click', () => { module.nodes=module.nodes.filter(item=>item.id!==node.id); editingNodeId=module.nodes[0]?.id??null; markDirty(); renderNodeEditor() })
  modal.querySelectorAll<HTMLElement>('[data-remove-asset]').forEach(button => button.addEventListener('click', () => { node.assets=node.assets.filter(asset=>asset.id!==button.dataset.removeAsset); markDirty(); renderNodeEditor() }))
  modal.querySelector<HTMLInputElement>('#asset-file')?.addEventListener('change', event => uploadSelectedAsset(node, event.target as HTMLInputElement))
}

async function uploadSelectedAsset(node: NarrationNode, input: HTMLInputElement, removedNarrationAudioEntry = false): Promise<void> {
  if (removedNarrationAudioEntry) return showToast('讲解音频上传功能已移除。')
  const file=input.files?.[0]; if(!file)return
  const modal=document.querySelector<HTMLElement>('#node-modal')!
  const kind=Number(modal.querySelector<HTMLSelectElement>('#asset-kind')?.value??0) as AssetKind
  if (kind !== 0 && kind !== 1) return showToast('当前仅支持上传视频或图片素材。')
  const duration=Number(modal.querySelector<HTMLInputElement>('#asset-duration')?.value??0)
  const progress=modal.querySelector<HTMLElement>('#upload-progress')!; progress.classList.add('visible')
  try {
    const asset=await api.uploadAsset(file,kind,duration,percent=>{const bar=progress.querySelector<HTMLElement>('span');if(bar)bar.style.width=`${percent}%`})
    node.assets.push(asset)
    markDirty()
    await syncDraftNow()
    renderNodeEditor(); showToast('素材上传成功。')
  } catch(error){progress.classList.remove('visible');showToast(error instanceof Error?error.message:'上传失败')}
}

function addNode(module: ExhibitionModule): void { const order=Math.max(0,...module.nodes.map(item=>item.order))+1; const node:NarrationNode={id:crypto.randomUUID(),name:`展示节点 ${order}`,order,narrationText:'',ttsAudioUrl:null,assets:[],failurePolicy:0,audioMixPolicy:1,videoVolume:1,narrationVolume:1,ttsConfiguration:null,narrationAudio:null}; module.nodes.push(node);editingNodeId=node.id;markDirty();renderNodeEditor() }
function closeNodeEditor(): void { document.querySelector('#node-modal')?.remove(); editingModuleId=null;editingNodeId=null;renderDashboard() }

function cloneUiConfig(source: UiExperienceConfig): UiExperienceConfig {
  return JSON.parse(JSON.stringify(source)) as UiExperienceConfig
}

type UiEditorSection = 'branding' | 'welcome' | 'led'

function openUiEditor(existingDraft?: UiExperienceConfig, initialSection: UiEditorSection = 'branding'): void {
  if (!uiConfig) return
  const config = existingDraft ? cloneUiConfig(existingDraft) : cloneUiConfig(uiConfig)
  config.layout ??= { touchTemplate: 'module-kiosk', ledTemplate: 'idle-media', touchShowHero: true, touchShowStatusPanel: true, touchShowQuickActions: true, ledShowBranding: config.ledShowBranding, ledShowStatus: config.ledShowStatus, touchWelcomeEnabled: true, touchIdleTimeoutSeconds: 180 }
  config.layout.touchWelcomeEnabled ??= true
  config.layout.touchIdleTimeoutSeconds ||= 180
  config.touchElements ??= []
  config.ledElements ??= []
  config.welcome ??= { audioEnabled: true, captions: [] }
  activeUiEditorDraft = config
  const findElement = (items: UiElementOverride[], key: string) => items.find(item => item.key === key)
  const welcomeEyebrow = findElement(config.touchElements, 'welcome.eyebrow')
  const welcomeTitle = findElement(config.touchElements, 'welcome.title')
  const welcomeSubtitle = findElement(config.touchElements, 'welcome.subtitle')
  const welcomeLogo = findElement(config.touchElements, 'welcome.logo')
  const welcomeAudio = findElement(config.touchElements, 'welcome.audio')
  const hasLedMedia = !!config.ledIdleMediaUrl && config.ledIdleMediaKind !== 'none'
  const ledMediaLabel = !hasLedMedia ? '尚未设置待机素材' : config.ledIdleMediaKind === 'video' ? '已设置循环视频' : '已设置背景图片'
  const ledMediaUrl = hasLedMedia ? escapeHtml(resolveAssetUrl(config.ledIdleMediaUrl!)) : ''
  const welcomeLogoUrl = welcomeLogo?.assetUrl ? escapeHtml(resolveAssetUrl(welcomeLogo.assetUrl)) : ''
  const welcomeAudioUrl = welcomeAudio?.assetUrl ? escapeHtml(resolveAssetUrl(welcomeAudio.assetUrl)) : ''
  const lastUpdated = config.version > 0 ? `${formatTime(config.updatedAtUtc)} · ${escapeHtml(displayAccountName(config.updatedBy))}` : '尚未保存过正式界面配置'
  const modal = document.createElement('div'); modal.id='ui-modal'; modal.className='modal-backdrop'
  modal.innerHTML=`<section class="ui-editor-modal"><header class="editor-header"><div><p class="eyebrow">终端外观与待机</p><h2>终端界面设置</h2></div><button id="close-ui" class="icon-button" aria-label="关闭终端界面设置">×</button></header>
    <div class="ui-editor-overview"><div><strong>服务器界面配置 V${config.version}</strong><span>最后保存：${lastUpdated}</span></div><div class="ui-editor-version"><span id="ui-dirty-state" class="ui-save-state">所有修改已保存</span><button id="reload-ui" type="button">重新读取</button></div></div>
    <div class="ui-editor-workspace">
      <nav class="ui-editor-nav" aria-label="终端界面设置分类">
        <button type="button" data-ui-section="branding"><span>01</span><strong>基本信息</strong><small>中控顶部品牌文字</small></button>
        <button type="button" data-ui-section="welcome"><span>02</span><strong>中控欢迎页</strong><small>欢迎内容、音频与字幕</small></button>
        <button type="button" data-ui-section="led"><span>03</span><strong>LED待机页</strong><small>图片或循环视频</small></button>
        <div class="ui-editor-note"><strong>说明</strong><p>此处设置与讲解内容版本相互独立。保存后终端将在约10秒内读取。</p></div>
      </nav>
      <div class="ui-editor-content">
        <section class="ui-config-panel" data-ui-panel="branding">
          <div class="ui-config-title"><div><strong>基本信息</strong><p>仅设置中控顶部品牌栏真实使用的系统名称和说明。</p></div><span>触控中控端</span></div>
          <div class="ui-fields"><label>系统名称<input id="touch-title" maxlength="80" value="${escapeHtml(config.touchTitle)}"/><small>显示在中控端顶部品牌栏。</small></label><label>终端说明<input id="touch-subtitle" maxlength="120" value="${escapeHtml(config.touchSubtitle)}"/><small>用于说明当前终端的产品定位。</small></label></div>
          <div class="ui-fixed-visual-note"><strong>首页视觉已锁定</strong><p>当前12模块首页采用已验收的深蓝科技主题。旧版背景、配色和模板字段继续兼容历史数据，但不再开放现场编辑，避免误操作破坏正式界面。</p></div>
          <div class="ui-brand-preview"><span>TG</span><div><strong id="ui-preview-title">${escapeHtml(config.touchTitle)}</strong><small id="ui-preview-subtitle">${escapeHtml(config.touchSubtitle)}</small></div></div>
        </section>
        <section class="ui-config-panel" data-ui-panel="welcome">
          <div class="ui-config-title"><div><strong>中控欢迎页</strong><p>管理欢迎文案、Logo、欢迎语音和180秒无操作返回逻辑。</p></div><span>真实欢迎流程</span></div>
          <div class="ui-fields ui-fields-single">
            <div class="ui-setting-row"><label class="check-field"><input id="touch-welcome-enabled" type="checkbox"/>启用欢迎页</label><label class="check-field"><input id="welcome-audio-enabled" type="checkbox"/>启用欢迎语音</label><label>无操作自动返回（秒）<input id="touch-idle-timeout" type="number" min="30" max="3600" step="10"/></label></div>
            <div class="field-row ui-two-columns"><label>品牌文字<input id="welcome-eyebrow" maxlength="80" value="${escapeHtml(welcomeEyebrow?.text ?? config.touchTitle)}"/></label><label>欢迎标题<input id="welcome-title" maxlength="200" value="${escapeHtml(welcomeTitle?.text ?? '欢迎开启自动讲解之旅')}"/></label></div>
            <label>欢迎说明<input id="welcome-subtitle" maxlength="200" value="${escapeHtml(welcomeSubtitle?.text ?? '轻触屏幕，开启展厅参观与自动讲解')}"/></label>
            <div class="media-setting ui-media-with-preview"><div>${welcomeLogoUrl?`<img src="${welcomeLogoUrl}" alt="当前欢迎页Logo"/>`:''}<section><strong>欢迎页Logo</strong><p>${welcomeLogo?.assetUrl?'已设置独立Logo':'未设置时使用安装包内置品牌Logo'}</p></section></div><label class="upload-button">上传或替换<input id="welcome-logo-file" type="file" accept="image/*"/></label><button id="clear-welcome-logo" ${welcomeLogo?.assetUrl?'':'disabled'}>移除</button></div>
            <div class="media-setting ui-media-with-preview"><div>${welcomeAudioUrl?`<audio controls preload="metadata" src="${welcomeAudioUrl}"></audio>`:''}<section><strong>欢迎词音频</strong><p>${welcomeAudio?.assetUrl?`已设置 · ${formatSize(welcomeAudio.assetSizeBytes??0)}`:'尚未上传；未启用语音时触碰后直接进入首页'}</p></section></div><label class="upload-button">上传或替换<input id="welcome-audio-file" type="file" accept="audio/mpeg,audio/wav,.mp3,.wav"/></label><button id="clear-welcome-audio" ${welcomeAudio?.assetUrl?'':'disabled'}>移除</button></div>
            <details class="ui-advanced-details"><summary>字幕时间轴（高级设置）</summary><p>每行填写：开始秒,结束秒,字幕文本。字幕将按照LED实际播放进度显示。</p><textarea id="welcome-captions" rows="6" placeholder="0,4,欢迎来到智慧展厅">${escapeHtml((config.welcome.captions ?? []).map(item => `${item.startSeconds},${item.endSeconds},${item.text}`).join('\n'))}</textarea></details>
          </div>
        </section>
        <section class="ui-config-panel" data-ui-panel="led">
          <div class="ui-config-title"><div><strong>LED待机页</strong><p>只设置实际播放端会使用的16:9待机图片或无停顿循环视频。</p></div><span>支持1920×1080与4K</span></div>
          <div class="ui-led-preview ${hasLedMedia?'has-media':'empty'}">${hasLedMedia?(config.ledIdleMediaKind==='video'?`<video muted controls preload="metadata" src="${ledMediaUrl}"></video>`:`<img src="${ledMediaUrl}" alt="当前LED待机图片"/>`):'<div><strong>尚未设置待机素材</strong><p>播放端将使用下方设置的纯色背景。</p></div>'}</div>
          <div class="media-setting"><div><strong>当前待机素材</strong><p>${ledMediaLabel}。讲解结束后LED播放端会自动返回此画面。</p></div><label class="upload-button">上传图片<input id="led-image-file" type="file" accept="image/*"/></label><label class="upload-button">上传视频<input id="led-video-file" type="file" accept="video/*,.mov,.mkv,.webm"/></label><button id="clear-led-media" ${hasLedMedia?'':'disabled'}>移除</button></div>
          <details class="ui-advanced-details"><summary>无素材时的兜底背景</summary><p>只有未设置待机图片/视频或素材无法显示时才使用此颜色。</p><label>背景颜色<input id="led-bg-color" type="color" value="${escapeHtml(config.ledBackgroundColor)}"/></label></details>
        </section>
        <div id="ui-upload-progress" class="upload-progress"><span></span></div>
      </div>
    </div>
    <footer class="editor-footer ui-editor-footer"><span id="ui-footer-message">界面配置独立保存，不会修改讲解内容版本。</span><button id="cancel-ui">放弃修改</button><button id="save-ui" class="primary" disabled>保存并应用到终端</button></footer>
  </section>`
  document.body.appendChild(modal)
  const upsertElement = (items: UiElementOverride[], key: string, text: string) => { const existing = findElement(items, key); if (existing) existing.text = text || null; else items.push({ key, text: text || null, visible: true }) }
  const read = () => {
    config.touchTitle = modal.querySelector<HTMLInputElement>('#touch-title')?.value.trim() || config.touchTitle
    config.touchSubtitle = modal.querySelector<HTMLInputElement>('#touch-subtitle')?.value.trim() || config.touchSubtitle
    config.ledBackgroundColor = modal.querySelector<HTMLInputElement>('#led-bg-color')?.value ?? config.ledBackgroundColor
    config.layout!.touchWelcomeEnabled = !!modal.querySelector<HTMLInputElement>('#touch-welcome-enabled')?.checked
    config.layout!.touchIdleTimeoutSeconds = Math.min(3600, Math.max(30, Number(modal.querySelector<HTMLInputElement>('#touch-idle-timeout')?.value) || 180))
    config.welcome = { audioEnabled: !!modal.querySelector<HTMLInputElement>('#welcome-audio-enabled')?.checked, captions: (modal.querySelector<HTMLTextAreaElement>('#welcome-captions')?.value ?? '').split(/\r?\n/).map(line => { const [start, end, ...text] = line.split(','); return { startSeconds: Number(start), endSeconds: Number(end), text: text.join(',').trim() } }).filter(cue => Number.isFinite(cue.startSeconds) && Number.isFinite(cue.endSeconds) && cue.endSeconds >= cue.startSeconds && cue.text.length > 0) }
    upsertElement(config.touchElements!, 'welcome.eyebrow', modal.querySelector<HTMLInputElement>('#welcome-eyebrow')?.value ?? '')
    upsertElement(config.touchElements!, 'welcome.title', modal.querySelector<HTMLInputElement>('#welcome-title')?.value ?? '')
    upsertElement(config.touchElements!, 'welcome.subtitle', modal.querySelector<HTMLInputElement>('#welcome-subtitle')?.value ?? '')
  }
  let activeSection: UiEditorSection = initialSection
  let uiDirty = !!existingDraft && JSON.stringify(config) !== JSON.stringify(uiConfig)
  const updateDirtyState = () => {
    const state = modal.querySelector<HTMLElement>('#ui-dirty-state')
    const footer = modal.querySelector<HTMLElement>('#ui-footer-message')
    const save = modal.querySelector<HTMLButtonElement>('#save-ui')
    if (state) { state.textContent = uiDirty ? '有未保存修改' : '所有修改已保存'; state.classList.toggle('dirty', uiDirty) }
    if (footer) footer.textContent = uiDirty ? '当前修改仅保存在本页面，点击保存后才会应用到终端。' : '界面配置独立保存，不会修改讲解内容版本。'
    if (save) save.disabled = !uiDirty
  }
  const markUiDirty = () => { uiDirty = true; updateDirtyState() }
  const showSection = (section: UiEditorSection) => {
    activeSection = section
    modal.querySelectorAll<HTMLElement>('[data-ui-panel]').forEach(panel => panel.hidden = panel.dataset.uiPanel !== section)
    modal.querySelectorAll<HTMLButtonElement>('[data-ui-section]').forEach(button => button.classList.toggle('active', button.dataset.uiSection === section))
  }
  modal.querySelectorAll<HTMLButtonElement>('[data-ui-section]').forEach(button => button.addEventListener('click', () => showSection(button.dataset.uiSection as UiEditorSection)))
  modal.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input:not([type=file]), textarea').forEach(input => {
    input.addEventListener('input', () => {
      markUiDirty()
      if (input.id === 'touch-title') modal.querySelector('#ui-preview-title')!.textContent = input.value
      if (input.id === 'touch-subtitle') modal.querySelector('#ui-preview-subtitle')!.textContent = input.value
    })
    input.addEventListener('change', markUiDirty)
  })
  const close = (force = false) => {
    if (!force && uiDirty && !window.confirm('当前界面设置尚未保存，确定放弃这些修改吗？')) return
    if (activeUiEditorDraft === config) activeUiEditorDraft = null
    modal.remove()
  }
  const clearWelcomeAsset = (key: string) => {
    read()
    const item=config.touchElements?.find(value=>value.key===key)
    if(item){item.assetUrl=null;item.assetId=null;item.assetSha256=null;item.assetSizeBytes=0;item.assetMediaType=null}
    modal.remove();openUiEditor(config, 'welcome')
  }
  modal.querySelector<HTMLInputElement>('#welcome-logo-file')?.addEventListener('change', event => { read(); void uploadUiElementAsset(event.target as HTMLInputElement, 'welcome.logo', true, modal, 1, '欢迎页Logo', 'welcome') })
  modal.querySelector<HTMLInputElement>('#welcome-audio-file')?.addEventListener('change', event => { read(); void uploadUiElementAsset(event.target as HTMLInputElement, 'welcome.audio', true, modal, 3, '欢迎词音频', 'welcome') })
  modal.querySelector('#clear-welcome-logo')?.addEventListener('click', () => clearWelcomeAsset('welcome.logo'))
  modal.querySelector('#clear-welcome-audio')?.addEventListener('click', () => clearWelcomeAsset('welcome.audio'))
  modal.querySelector('#reload-ui')?.addEventListener('click', async () => {
    if (uiDirty && !window.confirm('重新读取会丢弃当前未保存的修改，确定继续吗？')) return
    const button = modal.querySelector<HTMLButtonElement>('#reload-ui')
    if (button) button.disabled = true
    try {
      uiConfig = await api.getUi()
      modal.remove()
      openUiEditor()
      showToast('已重新读取服务器配置，未发布的修改已丢弃。')
    } catch (error) {
      if (button) button.disabled = false
      showToast(error instanceof Error ? error.message : '读取服务器配置失败')
    }
  })
  modal.querySelector('#close-ui')?.addEventListener('click',()=>close());modal.querySelector('#cancel-ui')?.addEventListener('click',()=>close())
  modal.querySelector('#clear-led-media')?.addEventListener('click',()=>{read();config.ledIdleMediaUrl=null;config.ledIdleMediaKind='none';modal.remove();openUiEditor(config,'led')})
  modal.querySelector<HTMLInputElement>('#led-image-file')?.addEventListener('change',event=>{read();void uploadUiAsset(event.target as HTMLInputElement,1,'led-image',modal,'led')})
  modal.querySelector<HTMLInputElement>('#led-video-file')?.addEventListener('change',event=>{read();void uploadUiAsset(event.target as HTMLInputElement,0,'led-video',modal,'led')})
  modal.querySelector('#save-ui')?.addEventListener('click',async()=>{read();const button=modal.querySelector<HTMLButtonElement>('#save-ui')!;button.disabled=true;button.textContent='正在保存…';try{uiConfig=await api.publishUi(config);uiDirty=false;close(true);showToast(`界面配置已保存为${formatVersion(uiConfig.version)}，终端将在约10秒内读取。`)}catch(error){button.disabled=false;button.textContent='保存并应用到终端';showToast(error instanceof Error?error.message:'界面配置保存失败')}})
  showSection(activeSection)
  modal.querySelector<HTMLInputElement>('#touch-welcome-enabled')!.checked = config.layout.touchWelcomeEnabled
  modal.querySelector<HTMLInputElement>('#welcome-audio-enabled')!.checked = config.welcome.audioEnabled
  modal.querySelector<HTMLInputElement>('#touch-idle-timeout')!.value = String(config.layout.touchIdleTimeoutSeconds)
  updateDirtyState()
}

async function uploadUiAsset(input:HTMLInputElement,kind:AssetKind,target:'led-image'|'led-video',modal:HTMLElement,reopenSection:UiEditorSection):Promise<void>{
  const file=input.files?.[0];const config=activeUiEditorDraft;if(!file||!config)return
  const progress=modal.querySelector<HTMLElement>('#ui-upload-progress')!;progress.classList.add('visible')
  try{await validateLedIdleMediaFile(file,target==='led-video');const asset=await api.uploadAsset(file,kind,0,percent=>{const bar=progress.querySelector<HTMLElement>('span');if(bar)bar.style.width=`${percent}%`});config.ledIdleMediaUrl=asset.url;config.ledIdleMediaKind=target==='led-video'?'video':'image';modal.remove();openUiEditor(config,reopenSection);showToast('待机素材已上传，但尚未应用；请点击“保存并应用到终端”。')}catch(error){progress.classList.remove('visible');showToast(error instanceof Error?error.message:'界面素材上传失败')}
}

async function validateLedIdleMediaFile(file: File, video: boolean): Promise<void> {
  const url = URL.createObjectURL(file)
  try {
    const dimensions = await new Promise<{ width: number; height: number }>((resolve, reject) => {
      if (video) {
        const element = document.createElement('video')
        element.preload = 'metadata'
        element.muted = true
        element.onloadedmetadata = () => resolve({ width: element.videoWidth, height: element.videoHeight })
        element.onerror = () => reject(new Error('无法读取视频信息，请确认文件可以正常播放。'))
        element.src = url
        return
      }
      const element = document.createElement('img')
      element.onload = () => resolve({ width: element.naturalWidth, height: element.naturalHeight })
      element.onerror = () => reject(new Error('无法读取图片信息，请确认文件格式正确。'))
      element.src = url
    })
    const error = validateLedIdleMediaDimensions(dimensions.width, dimensions.height)
    if (error) throw new Error(error)
  } finally {
    URL.revokeObjectURL(url)
  }
}
async function uploadUiElementAsset(input: HTMLInputElement, key: string, touch: boolean, modal: HTMLElement, kind: AssetKind = 1, label = '界面素材', reopenSection: UiEditorSection = 'welcome'): Promise<void> {
  const file = input.files?.[0]; const config = activeUiEditorDraft; if (!file || !config) return
  if (key === 'welcome.audio' && !/\.(mp3|wav)$/i.test(file.name)) {
    showToast('欢迎词仅支持MP3或WAV格式，请转换后重新上传。')
    input.value = ''
    return
  }
  const progress = modal.querySelector<HTMLElement>('#ui-upload-progress'); progress?.classList.add('visible')
  try {
    const asset = await api.uploadAsset(file, kind, 0, percent => { const bar = progress?.querySelector<HTMLElement>('span'); if (bar) bar.style.width = `${percent}%` })
    const items = touch ? (config.touchElements ??= []) : (config.ledElements ??= [])
    const existing = items.find(item => item.key === key)
    const value: UiElementOverride = { key, text: existing?.text ?? null, assetUrl: asset.url, color: existing?.color ?? null, visible: true, assetId: asset.id, assetSha256: asset.sha256, assetSizeBytes: asset.sizeBytes, assetMediaType: asset.mediaType ?? null }
    if (existing) Object.assign(existing, value); else items.push(value)
    modal.remove(); openUiEditor(config, reopenSection); showToast(`${label}已上传，但尚未应用；请点击“保存并应用到终端”。`)
  } catch (error) { progress?.classList.remove('visible'); showToast(error instanceof Error ? error.message : '界面素材上传失败') }
}

function markDirty(rerender=false): void {
  dirty = true
  publishReadiness = null
  draftMutationSequence++
  saveLocalDraft()
  scheduleDraftSync()
  if (rerender) renderDashboard()
  else root.querySelector<HTMLButtonElement>('#publish')?.setAttribute('disabled', '')
}
function addModule(): void { if(!content)return;const order=Math.max(0,...content.modules.map(item=>item.order))+1;content.modules.push({id:crypto.randomUUID(),name:'新模块',order,description:'',coverUrl:null,enabled:true,nodes:[],galleryImages:[]});markDirty(true) }

function validateDraft(modules: ExhibitionModule[]): string | null {
  for(const module of modules){if(!module.name.trim())return `第 ${module.order} 个模块名称不能为空。`;for(const node of module.nodes){if(!node.name.trim())return `${module.name}存在未命名节点。`;const hasPlayableVideo=node.assets.some(asset=>asset.kind===0||asset.kind===2);if(!hasPlayableVideo)return `${module.name} / ${node.name}需要至少上传一个可播放视频；图片素材可以同时保存。`}}
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
    draftStatuses = draft.narrationAudioStatuses; publishReadiness = draft.publishReadiness; draftConflict = ''; publishError = ''; dirty = false
    draftMutationSequence = 0; draftSyncedSequence = 0
    localStorage.removeItem(draftKey)
    versions = await api.contentVersions(); renderDashboard(); showToast(`${formatVersion(content.version)}发布成功。`)
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
        publishReadiness = snapshot.publishReadiness
        draftSyncedSequence = targetSequence
        draftConflict = ''
        refreshPublishReadiness()
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
  content = { ...content, modules: normalizeModuleGalleries(snapshot.modules) }
  draftBaseVersion = snapshot.baseContentVersion
  draftRevision = snapshot.revision
  draftStatuses = snapshot.narrationAudioStatuses
  publishReadiness = snapshot.publishReadiness
  draftConflict = ''
  draftMutationSequence++
  draftSyncedSequence = draftMutationSequence
  refreshPublishReadiness()
}

function refreshPublishReadiness(): void {
  const summary = root.querySelector<HTMLElement>('#content-publish-summary')
  if (summary) summary.outerHTML = renderContentPublishSummary()
  const button = root.querySelector<HTMLButtonElement>('#publish')
  if (!button) return
  button.disabled = !dirty || Boolean(draftConflict) || publishReadiness?.canPublish !== true
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
        return { content: { ...published, modules: normalizeModuleGalleries(needsSync ? local.modules : serverDraft.modules) }, dirty: true, needsSync }
      }
      localStorage.removeItem(draftKey)
    } else if (local) localStorage.removeItem(draftKey)
  } catch { localStorage.removeItem(draftKey) }
  const serverDirty = serverDraft.revision > 0
  return { content: { ...published, modules: normalizeModuleGalleries(serverDraft.modules) }, dirty: serverDirty, needsSync: false }
}
function normalizeModuleGalleries(modules: ExhibitionModule[]): ExhibitionModule[] {
  for (const module of modules) if (!Array.isArray(module.galleryImages)) module.galleryImages = []
  return modules
}
function showToast(message:string):void { let toast=root.querySelector<HTMLDivElement>('#toast');if(!toast){toast=document.createElement('div');toast.id='toast';toast.className='toast';root.appendChild(toast)}toast.textContent=message;toast.classList.add('visible');window.setTimeout(()=>toast?.classList.remove('visible'),3600) }

async function start():Promise<void>{if(!api.hasSession())return renderLogin();try{const me=await api.me();username=me.username;await loadDashboard()}catch{renderLogin('登录已过期，请重新登录。')}}
void start()
