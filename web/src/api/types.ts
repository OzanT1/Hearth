import type { components } from './schema';

// Server view models are derived from the generated OpenAPI schema.
export type User = Required<components['schemas']['UserDto']>;
export type Server = Required<components['schemas']['ServerDto']>;
export type Channel = Required<components['schemas']['ChannelDto']>;
export type Role = Required<components['schemas']['RoleDto']>;
export type Member = Required<components['schemas']['MemberDto']>;
export type Attachment = Required<components['schemas']['AttachmentDto']>;
export type Reaction = Required<components['schemas']['ReactionDto']>;
export type Message = Required<components['schemas']['MessageDto']>;
export type MessagePage = Required<components['schemas']['MessagePage']>;
export type Relationship = Required<components['schemas']['RelationshipDto']>;
export interface Invite { id: string; expiresAt: string; maxUses: number | null; uses: number; revoked: boolean }
