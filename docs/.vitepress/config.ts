import { defineConfig } from 'vitepress'

export default defineConfig({
  title: '显卡吧工具箱',
  description: '面向 PC 硬件爱好者与 DIY 玩家的 Windows 桌面工具集',
  lang: 'zh-CN',
  base: '/',
  appearance: false,
  head: [
    ['link', { rel: 'icon', href: '/icon.svg' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    ['script', {}, 'document.documentElement.classList.add("dark")']
  ],
  themeConfig: {
    logo: '/icon.svg',
    nav: [
      { text: '首页', link: '/' },
      { text: '指南', link: '/guide/' },
      { text: '更新日志', link: '/changelog' },
      { text: '下载', link: '/download' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: '指南',
          items: [
            { text: '简介', link: '/guide/' },
            { text: '快速开始', link: '/guide/getting-started' },
            { text: '工具管理', link: '/guide/tools' },
            { text: '代理工具', link: '/guide/proxy' },
            { text: '主题设置', link: '/guide/theme' },
            { text: '常见问题', link: '/guide/faq' }
          ]
        }
      ],
      '/features/': [
        {
          text: '功能',
          items: [
            { text: '概览', link: '/features/' },
            { text: '系统信息', link: '/features/system' },
            { text: '网络工具', link: '/features/network' },
            { text: '代理工具', link: '/features/proxy' },
            { text: '工具商店', link: '/features/store' }
          ]
        }
      ]
    },
    footer: {
      message: '基于 MIT 许可发布',
      copyright: '© 2025 显卡吧工具箱'
    },
    outline: {
      label: '页面导航'
    },
    docFooter: {
      prev: '上一页',
      next: '下一页'
    }
  }
})
