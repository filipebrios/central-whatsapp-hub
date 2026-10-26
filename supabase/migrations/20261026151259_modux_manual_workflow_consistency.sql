create or replace function app_private.agenda_queue_stamp()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  if tg_op='INSERT' then
    if new.state<>'preparada' then
      raise exception using errcode='22023',message='Uma nova mensagem começa pendente de envio manual';
    end if;
    new.created_by=(select auth.uid()); new.created_at=now();
    new.handled_by=null; new.handled_at=null;
  else
    if new.id is distinct from old.id or new.company_id is distinct from old.company_id
      or new.appointment_id is distinct from old.appointment_id or new.template_key is distinct from old.template_key
      or new.occurrence_key is distinct from old.occurrence_key or new.created_by is distinct from old.created_by
      or new.created_at is distinct from old.created_at then
      raise exception 'Identidade da mensagem não pode ser alterada';
    end if;
    if new.state is distinct from old.state then
      new.handled_by=(select auth.uid()); new.handled_at=now();
    else new.handled_by=old.handled_by; new.handled_at=old.handled_at;
    end if;
  end if;
  new.updated_at=now();
  return new;
end;
$$;

create or replace function app_private.agenda_close_stale_messages()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  -- Cancelar serviço ou mudar o horário invalida apenas rascunhos ainda não tratados.
  update public.agenda_message_queue q set state='cancelada'
  where q.company_id=new.company_id and q.appointment_id=new.id and q.state in ('preparada','erro') and (
    new.status in ('cancelado','cancelar') or
    (q.template_key in ('confirmacao','lembrete_d1','lembrete_d0','reagendamento') and
      (new.status='concluido' or q.occurrence_key is distinct from new.date::text||'T'||to_char(new.time,'HH24:MI'))) or
    (q.template_key in ('pos_venda','avaliacao_google') and
      (new.status<>'concluido' or q.occurrence_key is distinct from new.service_completed_date::text)) or
    (q.template_key='avaliacao_google' and coalesce(new.feedback_score,-1)<8)
  );
  if new.feedback_score is not null then
    update public.agenda_message_queue q set state='tratada_manual'
    where q.company_id=new.company_id and q.appointment_id=new.id and q.template_key='pos_venda'
      and q.state in ('preparada','erro');
  end if;
  return null;
end;
$$;
create trigger agenda_close_stale_messages after update of status,date,time,service_completed_date,feedback_score
on public.agenda_appointments for each row execute function app_private.agenda_close_stale_messages();

create or replace function app_private.agenda_template_stamp()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  if tg_op='UPDATE' and (new.company_id is distinct from old.company_id or new.key is distinct from old.key) then
    raise exception 'Identidade do modelo não pode ser alterada';
  end if;
  new.updated_by=(select auth.uid()); new.updated_at=now(); return new;
end;
$$;
create trigger agenda_template_stamp before insert or update on public.agenda_message_templates
for each row execute function app_private.agenda_template_stamp();

create or replace function app_private.agenda_catalog_stamp()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  if tg_op='UPDATE' and (new.company_id is distinct from old.company_id or new.id is distinct from old.id) then
    raise exception 'Identidade do serviço não pode ser alterada';
  end if;
  new.updated_at=now(); return new;
end;
$$;
create trigger agenda_catalog_stamp before insert or update on public.agenda_service_types
for each row execute function app_private.agenda_catalog_stamp();
