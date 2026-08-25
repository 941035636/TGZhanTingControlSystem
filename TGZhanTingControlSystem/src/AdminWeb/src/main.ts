import './style.css'
import { api, type AssetKind, type ClientRuntimeStatus, type ExhibitionModule, type NarrationNode, type PlaybackSessionStatus, type PublishedContent, type TtsStatus } from './api'

const app = document.querySelector<HTMLDivElement>('#app')
if (!app) throw new Error('App root was not found')
const root = app
let content: PublishedContent | null = null
let ttsStatus: TtsStatus = { provider: 'NotConfigured', voice: 'default', configured: false }
let username = ''
let dirty = false
let editingModuleId: string | null = null
let editingNodeId: string | null = null
let clientStatuses: ClientRuntimeStatus[] = []
let playbackSessions: PlaybackSessionStatus[] = []

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
    const [published, status, clients, sessions] = await Promise.all([api.getContent(), api.ttsStatus(), api.clientStatuses(), api.playbackSessions()])
    content = published; ttsStatus = status; clientStatuses = clients; playbackSessions = sessions; dirty = false; renderDashboard()
  } catch (error) { root.innerHTML = `<main class="error"><h1>无法连接管理服务</h1><p>${escapeHtml(error instanceof Error ? error.message : String(error))}</p></main>` }
}

function renderDashboard(): void {
  if (!content) return
  root.innerHTML = `<header class="topbar"><div><p class="eyebrow">TG EXHIBITION CONTROL</p><h1>展厅自动讲解管理平台</h1><p class="meta">正式版本 V${content.version} · ${content.modules.length} 个模块 · 发布人 ${escapeHtml(content.publishedBy || '系统初始化')}</p></div><div class="actions"><div class="tts-badge ${ttsStatus.configured ? 'ready' : ''}"><span>TTS服务</span><strong>${ttsStatus.configured ? escapeHtml(ttsStatus.provider) : '接口待配置'}</strong></div><div class="account"><span>当前账号</span><strong>${escapeHtml(username)}</strong></div><button id="logout">退出</button><button id="publish" class="primary" ${dirty ? '' : 'disabled'}>发布新版本</button></div></header>
    <main><section class="summary"><article><strong>${content.modules.filter(item => item.enabled).length}</strong><span>已启用模块</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.length, 0)}</strong><span>讲解节点</span></article><article><strong>${content.modules.reduce((sum, item) => sum + item.nodes.flatMap(node => node.assets).length + (item.nodes.filter(node => node.ttsAudioUrl).length), 0)}</strong><span>展示与音频素材</span></article><article><strong>${dirty ? '待发布' : '已同步'}</strong><span>编辑状态</span></article><article><strong>${clientStatuses.filter(client => client.online).length}/${Math.max(2, clientStatuses.length)}</strong><span>终端在线</span></article><article><strong>${playbackSessions.length}</strong><span>进行中讲解</span></article></section>
    <section class="section-title"><div><p class="eyebrow">MODULES</p><h2>讲解模块</h2><p class="section-note">配置模块、讲解节点、TTS文案和大屏素材，确认后发布到触控与LED终端。</p></div><button id="add">新增模块</button></section><section class="module-grid">${content.modules.sort((a,b) => a.order-b.order).map(moduleCard).join('')}</section></main><div id="toast" class="toast" aria-live="polite"></div>`
  root.querySelector('#publish')?.addEventListener('click', publish)
  root.querySelector('#add')?.addEventListener('click', addModule)
  root.querySelector('#logout')?.addEventListener('click', async () => { await api.logout(); content = null; username = ''; renderLogin() })
  root.querySelectorAll<HTMLElement>('[data-module]').forEach(bindCard)
}

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
  if (node) bindNodeForm(module, node, modal)
}

function nodeForm(node: NarrationNode): string {
  return `<div class="editor-fields"><div class="field-row"><label>节点名称<input id="node-name" value="${escapeHtml(node.name)}"/></label><label class="short-field">顺序<input id="node-order" type="number" min="1" value="${node.order}"/></label><label class="short-field">故障策略<select id="failure-policy"><option value="0" ${node.failurePolicy===0?'selected':''}>跳过继续</option><option value="1" ${node.failurePolicy===1?'selected':''}>停止讲解</option></select></label></div><label>讲解文案<textarea id="narration-text" class="narration-text" placeholder="输入供TTS合成和讲解员查看的完整文案">${escapeHtml(node.narrationText)}</textarea></label>
    <section class="tts-section"><div><strong>TTS讲解音频</strong><p>${ttsStatus.configured ? `已连接 ${escapeHtml(ttsStatus.provider)}，默认音色 ${escapeHtml(ttsStatus.voice)}` : '已预留TTS服务接口，等待配置供应商密钥；当前可上传已生成的讲解音频。'}</p></div><button id="generate-tts" ${ttsStatus.configured?'':'disabled'}>生成TTS</button><label class="upload-audio">上传音频<input id="audio-file" type="file" accept="audio/*"/></label></section>
    <div class="audio-url">${node.ttsAudioUrl ? `<span>已配置讲解音频</span><a href="${escapeHtml(node.ttsAudioUrl)}" target="_blank">${escapeHtml(node.ttsAudioUrl.split('/').pop())}</a><button id="remove-audio">移除</button>` : '<span>尚未配置讲解音频</span>'}</div>
    <section class="assets-section"><div class="assets-heading"><div><strong>LED大屏素材</strong><p>建议1920×1080 H.264 MP4；超大视频将直接流式写入服务器磁盘。</p></div></div><div class="upload-row"><select id="asset-kind"><option value="0">宣传视频</option><option value="1">展示图片</option><option value="2">动画素材</option></select><label>时长（秒）<input id="asset-duration" type="number" min="0" step="0.1" value="0"/></label><label class="upload-button">选择并上传<input id="asset-file" type="file" accept="video/*,image/*,.mov,.mkv,.webm"/></label><div id="upload-progress" class="upload-progress"><span></span></div></div><div class="asset-list">${node.assets.length ? node.assets.map(asset => `<article><div class="asset-icon">${asset.kind===0?'▶':asset.kind===1?'▧':'◆'}</div><div><strong>${escapeHtml(asset.name)}</strong><p>${assetKindName(asset.kind)} · ${formatSize(asset.sizeBytes)}${asset.durationSeconds?` · ${asset.durationSeconds}s`:''}</p></div><a href="${escapeHtml(asset.url)}" target="_blank">查看</a><button data-remove-asset="${asset.id}">移除</button></article>`).join('') : '<div class="empty-assets">尚未上传大屏素材</div>'}</div></section>
    <footer class="editor-footer"><span>修改保存在当前草稿中，点击主页面“发布新版本”后终端才会更新。</span><button id="delete-node" class="danger">删除当前节点</button><button id="done-node" class="primary">完成编辑</button></footer></div>`
}

function bindNodeForm(module: ExhibitionModule, node: NarrationNode, modal: HTMLElement): void {
  const bind = <T extends HTMLInputElement|HTMLTextAreaElement|HTMLSelectElement>(selector:string, update:(element:T)=>void) => modal.querySelector<T>(selector)?.addEventListener('input', event => { update(event.target as T); markDirty() })
  bind<HTMLInputElement>('#node-name', input => node.name=input.value)
  bind<HTMLInputElement>('#node-order', input => node.order=Math.max(1,Number(input.value)||1))
  bind<HTMLSelectElement>('#failure-policy', select => node.failurePolicy=Number(select.value) as 0|1)
  bind<HTMLTextAreaElement>('#narration-text', textarea => node.narrationText=textarea.value)
  modal.querySelector('#done-node')?.addEventListener('click', closeNodeEditor)
  modal.querySelector('#delete-node')?.addEventListener('click', () => { module.nodes=module.nodes.filter(item=>item.id!==node.id); editingNodeId=module.nodes[0]?.id??null; markDirty(); renderNodeEditor() })
  modal.querySelector('#remove-audio')?.addEventListener('click', () => { node.ttsAudioUrl=null; markDirty(); renderNodeEditor() })
  modal.querySelectorAll<HTMLElement>('[data-remove-asset]').forEach(button => button.addEventListener('click', () => { node.assets=node.assets.filter(asset=>asset.id!==button.dataset.removeAsset); markDirty(); renderNodeEditor() }))
  modal.querySelector<HTMLInputElement>('#asset-file')?.addEventListener('change', event => uploadSelectedAsset(node, event.target as HTMLInputElement, false))
  modal.querySelector<HTMLInputElement>('#audio-file')?.addEventListener('change', event => uploadSelectedAsset(node, event.target as HTMLInputElement, true))
  modal.querySelector('#generate-tts')?.addEventListener('click', async () => { try { const result=await api.synthesize(node.narrationText,ttsStatus.voice); node.ttsAudioUrl=result.audioUrl; markDirty(); renderNodeEditor() } catch(error){showToast(error instanceof Error?error.message:'TTS生成失败')} })
}

async function uploadSelectedAsset(node: NarrationNode, input: HTMLInputElement, narrationAudio: boolean): Promise<void> {
  const file=input.files?.[0]; if(!file)return
  const modal=document.querySelector<HTMLElement>('#node-modal')!
  const kind=narrationAudio?3:Number(modal.querySelector<HTMLSelectElement>('#asset-kind')?.value??0) as AssetKind
  const duration=narrationAudio?0:Number(modal.querySelector<HTMLInputElement>('#asset-duration')?.value??0)
  const progress=modal.querySelector<HTMLElement>('#upload-progress')!; progress.classList.add('visible')
  try {
    const asset=await api.uploadAsset(file,kind,duration,percent=>{const bar=progress.querySelector<HTMLElement>('span');if(bar)bar.style.width=`${percent}%`})
    if(narrationAudio) node.ttsAudioUrl=asset.url; else node.assets.push(asset)
    markDirty(); renderNodeEditor(); showToast(`${file.name} 上传成功。`)
  } catch(error){progress.classList.remove('visible');showToast(error instanceof Error?error.message:'上传失败')}
}

function addNode(module: ExhibitionModule): void { const order=Math.max(0,...module.nodes.map(item=>item.order))+1; const node:NarrationNode={id:crypto.randomUUID(),name:`讲解节点 ${order}`,order,narrationText:'',ttsAudioUrl:null,assets:[],failurePolicy:0}; module.nodes.push(node);editingNodeId=node.id;markDirty();renderNodeEditor() }
function closeNodeEditor(): void { document.querySelector('#node-modal')?.remove(); editingModuleId=null;editingNodeId=null;renderDashboard() }
function markDirty(rerender=false): void { dirty=true; if(rerender)renderDashboard(); else root.querySelector<HTMLButtonElement>('#publish')?.removeAttribute('disabled') }
function addModule(): void { if(!content)return;const order=Math.max(0,...content.modules.map(item=>item.order))+1;content.modules.push({id:crypto.randomUUID(),name:'新模块',order,description:'',coverUrl:null,enabled:true,nodes:[]});dirty=true;renderDashboard() }

function validateDraft(modules: ExhibitionModule[]): string | null {
  for(const module of modules){if(!module.name.trim())return `第 ${module.order} 个模块名称不能为空。`;for(const node of module.nodes){if(!node.name.trim())return `${module.name}存在未命名节点。`;if(!node.narrationText.trim()&&!node.ttsAudioUrl)return `${module.name} / ${node.name}需要讲解文案或讲解音频。`}}
  return null
}

async function publish(): Promise<void> { if(!content)return;const validation=validateDraft(content.modules);if(validation)return showToast(validation);try{content=await api.publish(content.modules);dirty=false;renderDashboard();showToast(`版本 V${content.version} 发布成功。`)}catch(error){if(!api.hasSession()){renderLogin('登录已过期，请重新登录。');return}showToast(error instanceof Error?error.message:'发布失败')} }
function showToast(message:string):void { let toast=root.querySelector<HTMLDivElement>('#toast');if(!toast){toast=document.createElement('div');toast.id='toast';toast.className='toast';root.appendChild(toast)}toast.textContent=message;toast.classList.add('visible');window.setTimeout(()=>toast?.classList.remove('visible'),3600) }

async function start():Promise<void>{if(!api.hasSession())return renderLogin();try{const me=await api.me();username=me.username;await loadDashboard()}catch{renderLogin('登录已过期，请重新登录。')}}
void start()
